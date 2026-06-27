using Kombats.Abstractions;
using Kombats.Abstractions.Auth;
using Kombats.Matchmaking.Api.Endpoints;
using Kombats.Matchmaking.Api.Extensions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;
using Kombats.Matchmaking.Application.UseCases.GetQueueStatus;
using Kombats.Matchmaking.Application.UseCases.Heartbeat;
using Kombats.Matchmaking.Application.UseCases.JoinQueue;
using Kombats.Matchmaking.Application.UseCases.LeaveQueue;
using Kombats.Matchmaking.Application.UseCases.TimeoutStaleMatches;
using Kombats.Matchmaking.Bootstrap.Workers;
using Kombats.Matchmaking.Infrastructure;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Messaging;
using Kombats.Matchmaking.Infrastructure.Messaging.Consumers;
using Kombats.Matchmaking.Infrastructure.Options;
using Kombats.Matchmaking.Infrastructure.Persistence;
using Kombats.Matchmaking.Infrastructure.Redis;
using Kombats.Matchmaking.Infrastructure.Repositories;
using Kombats.Messaging.DependencyInjection;
using Kombats.Observability;
using Kombats.Players.Contracts;
using Kombats.Battle.Contracts.Battle;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

// Authentication & Authorization
builder.Services.AddKombatsAuth(builder.Configuration);

// API Documentation
builder.Services.AddMatchmakingApiDocumentation();

// Endpoints (scan Api assembly)
var apiAssembly = typeof(IEndpoint).Assembly;
builder.Services.AddEndpoints(apiAssembly);

// Validation
builder.Services.AddMatchmakingValidation(apiAssembly);

// Identity
builder.Services.AddCurrentIdentity();

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

// Application handlers (direct DI — no MediatR)
builder.Services.AddScoped<ICommandHandler<JoinQueueCommand, JoinQueueResult>, JoinQueueHandler>();
builder.Services.AddScoped<ICommandHandler<LeaveQueueCommand, LeaveQueueResult>, LeaveQueueHandler>();
builder.Services.AddScoped<ICommandHandler<HeartbeatCommand>, HeartbeatHandler>();
builder.Services.AddScoped<IQueryHandler<GetQueueStatusQuery, QueueStatusResult>, GetQueueStatusHandler>();
builder.Services.AddScoped<ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>, ExecuteMatchmakingTickHandler>();
builder.Services.AddScoped<ICommandHandler<TimeoutStaleMatchesCommand, int>, TimeoutStaleMatchesHandler>();

// Infrastructure — persistence
builder.Services.AddDbContext<MatchmakingDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("PostgresConnection")
                           ?? throw new InvalidOperationException("PostgresConnection connection string is required.");
    options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", MatchmakingDbContext.Schema)
                  .EnableRetryOnFailure())
        .UseSnakeCaseNamingConvention()
        .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>();
});

builder.Services.AddScoped<IMatchRepository, MatchRepository>();
builder.Services.AddScoped<IPlayerCombatProfileRepository, PlayerCombatProfileRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<ICreateBattlePublisher, MassTransitCreateBattlePublisher>();

// Infrastructure — Redis (Sentinel-ready: use "sentinel1:26379,sentinel2:26379,serviceName=mymaster,defaultDatabase=1" for production)
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
redisConfig.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));

builder.Services.Configure<MatchmakingRedisOptions>(
    builder.Configuration.GetSection(MatchmakingRedisOptions.SectionName));

builder.Services.AddScoped<IMatchQueueStore, RedisMatchQueueStore>();
builder.Services.AddScoped<IPlayerMatchStatusStore, RedisPlayerMatchStatusStore>();
builder.Services.AddScoped<IQueuePresenceStore, RedisQueuePresenceStore>();

// Infrastructure — lease
builder.Services.AddSingleton<InstanceIdService>();
builder.Services.AddSingleton<RedisLeaseLock>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    var logger = sp.GetRequiredService<ILogger<RedisLeaseLock>>();
    var redisOptions = sp.GetRequiredService<IOptions<MatchmakingRedisOptions>>();
    return new RedisLeaseLock(redis, logger, redisOptions.Value.DatabaseIndex);
});
builder.Services.AddSingleton<MatchmakingLeaseService>();

// Infrastructure — worker options
builder.Services.Configure<MatchmakingWorkerOptions>(
    builder.Configuration.GetSection(MatchmakingWorkerOptions.SectionName));
builder.Services.Configure<MatchTimeoutWorkerOptions>(
    builder.Configuration.GetSection(MatchTimeoutWorkerOptions.SectionName));
builder.Services.Configure<QueuePresenceOptions>(
    builder.Configuration.GetSection(QueuePresenceOptions.SectionName));

// Messaging (Kombats.Messaging with transactional outbox — AD-01)
builder.Services.AddMessaging<MatchmakingDbContext>(
    builder.Configuration,
    "matchmaking",
    configureConsumers: bus =>
    {
        bus.AddConsumer<PlayerCombatProfileChangedConsumer>();
        bus.AddConsumer<BattleCreatedConsumer>();
        bus.AddConsumer<BattleCompletedConsumer>();
    },
    configure: messagingBuilder =>
    {
        messagingBuilder.Map<PlayerCombatProfileChanged>("PlayerCombatProfileChanged");
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
builder.Services.AddKombatsObservability(builder.Configuration, "matchmaking");

// Background workers
builder.Services.AddHostedService<MatchmakingPairingWorker>();
builder.Services.AddHostedService<MatchTimeoutWorker>();
builder.Services.AddHostedService<QueuePresenceSweepWorker>();

// Global exception handling — RFC 7807 ProblemDetails
builder.Services.AddProblemDetails();

var app = builder.Build();

// NOTE: No Database.MigrateAsync() on startup — AD-13 forbids it.

app.UseExceptionHandler();

app.UseMatchmakingApiDocumentation();

app.UseHttpsRedirection();

app.UseSerilogRequestLogging();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();
