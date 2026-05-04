using Kombats.Abstractions;
using Kombats.Abstractions.Auth;
using Kombats.Chat.Api.Endpoints;
using Kombats.Chat.Api.Extensions;
using Kombats.Chat.Api.Hubs;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.ConnectUser;
using Kombats.Chat.Application.UseCases.DisconnectUser;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.GetConversations;
using Kombats.Chat.Application.UseCases.GetDirectMessages;
using Kombats.Chat.Application.UseCases.GetOnlinePlayers;
using Kombats.Chat.Application.UseCases.HandlePlayerProfileChanged;
using Kombats.Chat.Application.UseCases.JoinGlobalChat;
using Kombats.Chat.Application.UseCases.SendDirectMessage;
using Kombats.Chat.Application.UseCases.SendGlobalMessage;
using Kombats.Chat.Infrastructure.Data;
using Kombats.Chat.Infrastructure.Data.Repositories;
using Kombats.Chat.Infrastructure.Messaging.Consumers;
using Kombats.Chat.Infrastructure.Options;
using Kombats.Chat.Infrastructure.Redis;
using Kombats.Chat.Infrastructure.Workers;
using Kombats.Chat.Infrastructure.Services;
using Kombats.Messaging.DependencyInjection;
using Kombats.Players.Contracts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

// Authentication & Authorization
builder.Services.AddKombatsAuth(builder.Configuration);
builder.Services.AddHttpContextAccessor();

// Validation & Endpoints (scan Api assembly)
var apiAssembly = typeof(IEndpoint).Assembly;
builder.Services.AddEndpoints(apiAssembly);

// API Documentation
builder.Services.AddApiDocumentation();

// CORS — log the active environment and chosen branch so local-dev startup
// issues (e.g. Rider/IDE run profile not setting ASPNETCORE_ENVIRONMENT) are
// diagnosable from the first lines of output.
bool corsDevBranch = builder.Environment.IsDevelopment();
Console.WriteLine(
    $"[Kombats.Chat.Bootstrap] EnvironmentName='{builder.Environment.EnvironmentName}' " +
    $"CORS branch='{(corsDevBranch ? "Development (permissive localhost)" : "Non-Development (Cors:AllowedOrigins required)")}'");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsDevBranch)
        {
            // Development-only: allow typical local dev origins (test client,
            // Vite/CRA/Next dev servers, BFF, sibling services) with credentials
            // so SignalR and cookie-based flows work. Fail-closed behavior for
            // non-Development is preserved in the else branch below.
            policy.WithOrigins(
                    "http://localhost:3000", "https://localhost:3000",
                    "http://localhost:5000", "https://localhost:5000",
                    "http://localhost:5001", "https://localhost:5001",
                    "http://localhost:5002", "https://localhost:5002",
                    "http://localhost:5003", "https://localhost:5003",
                    "http://localhost:5004", "https://localhost:5004",
                    "http://localhost:5173", "https://localhost:5173",
                    "http://localhost:8080", "https://localhost:8080",
                    "http://127.0.0.1:3000", "https://127.0.0.1:3000",
                    "http://127.0.0.1:5173", "https://127.0.0.1:5173")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
        else
        {
            var origins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>();

            if (origins is null || origins.Length == 0)
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must be configured in non-Development environments. " +
                    "Set it to an array of allowed origin URLs in appsettings.json.");

            policy.WithOrigins(origins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

// Health checks — readiness covers every hard runtime dependency of delivered chat
// functionality: Postgres (messages/conversations), Redis (presence, rate-limit, cache),
// and RabbitMQ (MassTransit consumer + outbox publication).
var postgresConnection = builder.Configuration.GetConnectionString("PostgresConnection")
    ?? "Host=localhost;Port=5432;Database=kombats;Username=postgres;Password=postgres";
var redisHealthConnection = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379,abortConnect=false";
var rabbitOptions = builder.Configuration
    .GetSection(Kombats.Messaging.Options.MessagingOptions.SectionName)
    .Get<Kombats.Messaging.Options.MessagingOptions>()?.RabbitMq
    ?? new Kombats.Messaging.Options.RabbitMqOptions();
string rabbitScheme = rabbitOptions.UseTls ? "amqps" : "amqp";
string rabbitHost = string.IsNullOrWhiteSpace(rabbitOptions.Host) ? "localhost" : rabbitOptions.Host;
string rabbitVHost = string.IsNullOrWhiteSpace(rabbitOptions.VirtualHost) ? "/" : rabbitOptions.VirtualHost;
string rabbitUser = Uri.EscapeDataString(rabbitOptions.Username);
string rabbitPass = Uri.EscapeDataString(rabbitOptions.Password);
var rabbitUri = new Uri(
    $"{rabbitScheme}://{rabbitUser}:{rabbitPass}@{rabbitHost}:{rabbitOptions.Port}/{Uri.EscapeDataString(rabbitVHost.TrimStart('/'))}");
var rabbitConnectionFactory = new RabbitMQ.Client.ConnectionFactory
{
    Uri = rabbitUri,
    RequestedHeartbeat = TimeSpan.FromSeconds(rabbitOptions.HeartbeatSeconds),
    AutomaticRecoveryEnabled = true,
};
builder.Services.AddSingleton<RabbitMQ.Client.IConnectionFactory>(rabbitConnectionFactory);
builder.Services.AddSingleton<RabbitMQ.Client.IConnection>(sp =>
    sp.GetRequiredService<RabbitMQ.Client.IConnectionFactory>()
        .CreateConnectionAsync().GetAwaiter().GetResult());

builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnection, name: "postgresql")
    .AddRedis(redisHealthConnection, name: "redis")
    .AddRabbitMQ(name: "rabbitmq");

// OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Kombats.Chat"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Npgsql");

        string? otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

// === Batch 1: EF Core + Repositories + Handlers ===

builder.Services.AddDbContext<ChatDbContext>(options =>
{
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnection"), npgsql =>
        {
            npgsql.MigrationsHistoryTable("__ef_migrations_history", ChatDbContext.Schema);
            npgsql.EnableRetryOnFailure();
        })
        .UseSnakeCaseNamingConvention()
        .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>();
});

// Application handlers (Batch 1: read-path only)
builder.Services.AddScoped<IQueryHandler<GetConversationMessagesQuery, GetConversationMessagesResponse>, GetConversationMessagesHandler>();
builder.Services.AddScoped<IQueryHandler<GetConversationsQuery, GetConversationsResponse>, GetConversationsHandler>();
builder.Services.AddScoped<IQueryHandler<GetDirectMessagesQuery, GetConversationMessagesResponse>, GetDirectMessagesHandler>();

// Infrastructure repositories
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();

// === Batch 2: Redis + Presence + Rate Limiter + Player Info + Resolvers ===

// Redis connection (DB 2 for Chat)
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnectionString));

// Redis port implementations
builder.Services.AddScoped<IPresenceStore, RedisPresenceStore>();
builder.Services.AddScoped<IRateLimiter, RedisRateLimiter>();
builder.Services.AddScoped<IPlayerInfoCache, RedisPlayerInfoCache>();

// HTTP client for Players service. The forwarding handler attaches the caller's
// bearer token (from HttpContext, saved via JwtBearerOptions.SaveToken) so the
// authenticated Players profile endpoint doesn't 401 on cache-miss fallback.
builder.Services.AddTransient<Kombats.Chat.Bootstrap.Http.PlayersAuthForwardingHandler>();
builder.Services.AddHttpClient("Players", client =>
{
    var playersBaseUrl = builder.Configuration["Players:BaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(playersBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
})
.AddHttpMessageHandler<Kombats.Chat.Bootstrap.Http.PlayersAuthForwardingHandler>();

// Application services
builder.Services.AddScoped<IDisplayNameResolver, DisplayNameResolver>();
builder.Services.AddScoped<IEligibilityChecker, EligibilityChecker>();

// Application handler (Batch 2: online players query)
builder.Services.AddScoped<IQueryHandler<GetOnlinePlayersQuery, GetOnlinePlayersResponse>, GetOnlinePlayersHandler>();

// === Batch 3: SignalR hub + chat command handlers + filters ===

builder.Services.AddSingleton(TimeProvider.System);

// Filters / restriction
builder.Services.AddScoped<IMessageFilter, MessageFilter>();
builder.Services.AddScoped<IUserRestriction, UserRestriction>();

// Chat command handlers
builder.Services.AddScoped<ICommandHandler<ConnectUserCommand>, ConnectUserHandler>();
builder.Services.AddScoped<ICommandHandler<DisconnectUserCommand>, DisconnectUserHandler>();
builder.Services.AddScoped<ICommandHandler<JoinGlobalChatCommand, JoinGlobalChatResponse>, JoinGlobalChatHandler>();
builder.Services.AddScoped<ICommandHandler<SendGlobalMessageCommand>, SendGlobalMessageHandler>();
builder.Services.AddScoped<ICommandHandler<SendDirectMessageCommand, SendDirectMessageResponse>, SendDirectMessageHandler>();

// SignalR + chat notifier (singleton scope: hub context is safe to capture)
builder.Services.AddSignalR();
builder.Services.AddSingleton<IChatNotifier, SignalRChatNotifier>();
builder.Services.AddSingleton<HeartbeatScheduler>();

// === Batch 4: MassTransit consumer + background workers ===

// Additional command handler
builder.Services.AddScoped<ICommandHandler<HandlePlayerProfileChangedCommand>, HandlePlayerProfileChangedHandler>();

// Worker options
builder.Services.Configure<MessageRetentionOptions>(
    builder.Configuration.GetSection(MessageRetentionOptions.SectionName));
builder.Services.Configure<PresenceSweepOptions>(
    builder.Configuration.GetSection(PresenceSweepOptions.SectionName));

// Messaging (Kombats.Messaging with transactional outbox/inbox — AD-01)
builder.Services.AddMessaging<ChatDbContext>(
    builder.Configuration,
    "chat",
    configureConsumers: bus =>
    {
        bus.AddConsumer<PlayerCombatProfileChangedConsumer>();
    },
    configure: messagingBuilder =>
    {
        messagingBuilder.Map<PlayerCombatProfileChanged>("PlayerCombatProfileChanged");
    });

// Hosted workers
builder.Services.AddHostedService<MessageRetentionWorker>();
builder.Services.AddHostedService<PresenceSweepWorker>();

var app = builder.Build();

// NOTE: No Database.MigrateAsync() on startup — AD-13 forbids it.

app.UseMiddleware<Kombats.Chat.Api.Middleware.ExceptionHandlingMiddleware>();

app.UseApiDocumentation();

app.UseHttpsRedirection();

app.UseSerilogRequestLogging();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();

app.MapHub<InternalChatHub>("/chathub-internal");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();

// Required for WebApplicationFactory<Program> in API tests.
public partial class Program;
