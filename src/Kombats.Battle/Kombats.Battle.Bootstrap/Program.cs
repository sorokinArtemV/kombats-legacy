using System.Text.Json.Serialization;
using Kombats.Abstractions.Auth;
using Kombats.Battle.Infrastructure.Configuration;
using Kombats.Battle.Api.Endpoints;
using Kombats.Battle.Api.Extensions;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Application.UseCases.GetBattleHistory;
using Kombats.Battle.Application.UseCases.Recovery;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Bootstrap.Workers;
using Kombats.Battle.Contracts.Battle;
using Kombats.Battle.Domain.Engine;
using Kombats.Battle.Infrastructure.Data;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Messaging.Consumers;
using Kombats.Battle.Infrastructure.Messaging.Projections;
using Kombats.Battle.Infrastructure.Messaging.Publisher;
using Kombats.Battle.Infrastructure.Realtime.SignalR;
using Kombats.Battle.Infrastructure.Rules;
using Kombats.Battle.Infrastructure.State.Redis;
using Kombats.Battle.Infrastructure.Time;
using Kombats.Messaging.DependencyInjection;
using Kombats.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

// Authentication & Authorization
builder.Services.AddKombatsAuth(builder.Configuration);

// API Documentation — registers a Bearer security scheme and global security
// requirement so Scalar's "Authorize" flow works against Keycloak-issued JWTs.
builder.Services.AddBattleApiDocumentation();

// Global error handling — RFC 7807 ProblemDetails
builder.Services.AddProblemDetails();

// Endpoints
var apiAssembly = typeof(IEndpoint).Assembly;
builder.Services.AddEndpoints(apiAssembly);

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            var origins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>();

            if (origins is null || origins.Length == 0)
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must be configured in non-Development environments.");

            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

// Domain services
builder.Services.AddScoped<IBattleEngine, BattleEngine>();

// Application services (direct DI — no MediatR)
builder.Services.AddScoped<IActionIntake, ActionIntakeService>();
builder.Services.AddScoped<BattleLifecycleAppService>();
builder.Services.AddScoped<BattleTurnAppService>();
builder.Services.AddScoped<BattleRecoveryService>();
builder.Services.AddScoped<GetBattleHistoryHandler>();

// Infrastructure — persistence
builder.Services.AddDbContext<BattleDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("PostgresConnection")
                           ?? throw new InvalidOperationException("PostgresConnection connection string is required.");
    options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", BattleDbContext.Schema)
                .EnableRetryOnFailure())
        .UseSnakeCaseNamingConvention()
        .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>();
});

// Infrastructure — Redis (Sentinel-ready: use "sentinel1:26379,sentinel2:26379,serviceName=mymaster" for production)
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
redisConfig.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));

// Infrastructure — options
builder.Services.Configure<BattleRedisOptions>(
    builder.Configuration.GetSection(BattleRedisOptions.SectionName));
builder.Services.Configure<BattleRulesetsOptions>(
    builder.Configuration.GetSection(BattleRulesetsOptions.SectionName));
builder.Services.AddOptions<BattleRulesetsOptions>()
    .Bind(builder.Configuration.GetSection(BattleRulesetsOptions.SectionName))
    .Validate(RulesetsOptionsValidator.Validate)
    .ValidateOnStart();
builder.Services.Configure<BattleRewardsOptions>(
    builder.Configuration.GetSection(BattleRewardsOptions.SectionName));

// Infrastructure — ports
builder.Services.AddScoped<IBattleTurnHistoryStore, BattleTurnHistoryStore>();
builder.Services.AddScoped<IBattleHistoryRepository, BattleHistoryRepository>();
builder.Services.AddScoped<IBattleRecoveryRepository, BattleRecoveryRepository>();
builder.Services.AddScoped<IBattleUnitOfWork, BattleUnitOfWork>();
builder.Services.AddScoped<IBattleStateStore, RedisBattleStateStore>();
builder.Services.AddScoped<IBattleRealtimeNotifier, SignalRBattleRealtimeNotifier>();
builder.Services.AddScoped<IBattleEventPublisher, MassTransitBattleEventPublisher>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IRulesetProvider, RulesetProvider>();
builder.Services.AddSingleton<ISeedGenerator, SeedGenerator>();

// Infrastructure — SignalR
// Backplane reuses the same Redis instance as battle state today; production deployments should split the two onto dedicated Redis nodes.
builder.Services.AddSignalR(options => { options.EnableDetailedErrors = builder.Environment.IsDevelopment(); })
    .AddJsonProtocol(options => { options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()); })
    .AddStackExchangeRedis(redisConnectionString);

// Messaging (Kombats.Messaging with transactional outbox — AD-01)
builder.Services.AddMessaging<BattleDbContext>(
    builder.Configuration,
    "battle",
    configureConsumers: bus =>
    {
        bus.AddConsumer<CreateBattleConsumer>();
        bus.AddConsumer<BattleCompletedProjectionConsumer>();
    },
    configure: messagingBuilder =>
    {
        messagingBuilder.Map<CreateBattle>("CreateBattle");
        messagingBuilder.Map<BattleCreated>("BattleCreated");
        messagingBuilder.Map<BattleCompleted>("BattleCompleted");
    });

// Health checks (MassTransit contributes RabbitMQ health check automatically)
var postgresConnection = builder.Configuration.GetConnectionString("PostgresConnection")
    ?? "Host=localhost;Port=5432;Database=kombats;Username=postgres;Password=postgres";
builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnection, name: "postgresql")
    .AddRedis(redisConnectionString, name: "redis");

// Observability (OpenTelemetry tracing + metrics + KombatsMetrics singleton).
// Redis tracing instrumentation discovers IConnectionMultiplexer via DI, so this call
// must come after the IConnectionMultiplexer registration above.
builder.Services.AddKombatsObservability(builder.Configuration, "battle");

// Background workers
builder.Services.Configure<TurnDeadlineWorkerOptions>(
    builder.Configuration.GetSection("Battle:TurnDeadlineWorker"));
builder.Services.AddHostedService<TurnDeadlineWorker>();

builder.Services.Configure<BattleRecoveryWorkerOptions>(
    builder.Configuration.GetSection("Battle:RecoveryWorker"));
builder.Services.AddHostedService<BattleRecoveryWorker>();

var app = builder.Build();

// NOTE: No Database.MigrateAsync() on startup — AD-13 forbids it.

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseBattleApiDocumentation();

app.UseHttpsRedirection();

app.UseSerilogRequestLogging();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();
app.MapHub<BattleHub>("/battlehub");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();

// Expose for WebApplicationFactory<Program> in integration tests
public partial class Program;
