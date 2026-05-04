using System.Reflection;
using Kombats.Abstractions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Messaging.DependencyInjection;
using Kombats.Players.Contracts;
using Kombats.Abstractions.Auth;
using Kombats.Players.Api.Extensions;
using Kombats.Players.Application;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.Battles;
using Kombats.Players.Application.UseCases.AllocateStatPoints;
using Kombats.Players.Application.UseCases.ChangeAvatar;
using Kombats.Players.Application.UseCases.EnsureCharacterExists;
using Kombats.Players.Application.UseCases.GetCharacter;
using Kombats.Players.Application.UseCases.GetPlayerProfile;
using Kombats.Players.Application.UseCases.SetCharacterName;
using Kombats.Players.Infrastructure.Configuration;
using Kombats.Players.Infrastructure.Data;
using Kombats.Players.Infrastructure.Messaging;
using Kombats.Players.Infrastructure.Messaging.Consumers;
using Kombats.Players.Infrastructure.Persistence.EF;
using Kombats.Players.Infrastructure.Persistence.Repository;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Kombats.Players.Api.Middleware;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

// Authentication & Authorization
builder.Services.AddKombatsAuth(builder.Configuration);
builder.Services.AddCurrentIdentity();

// Validation & Endpoints (scan Api assembly)
var apiAssembly = typeof(Kombats.Players.Api.Endpoints.IEndpoint).Assembly;
builder.Services.AddValidation(apiAssembly);
builder.Services.AddEndpoints(apiAssembly);

// API Documentation
builder.Services.AddApiDocumentation();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
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

// Domain configuration
builder.Services.Configure<LevelingOptions>(
    builder.Configuration.GetSection("Leveling"));

// Application handlers
builder.Services.AddScoped<ICommandHandler<AllocateStatPointsCommand, AllocateStatPointsResult>, AllocateStatPointsHandler>();
builder.Services.AddScoped<ICommandHandler<ChangeAvatarCommand, ChangeAvatarResult>, ChangeAvatarHandler>();
builder.Services.AddScoped<ICommandHandler<EnsureCharacterExistsCommand, CharacterStateResult>, EnsureCharacterExistsHandler>();
builder.Services.AddScoped<ICommandHandler<SetCharacterNameCommand, CharacterStateResult>, SetCharacterNameHandler>();
builder.Services.AddScoped<IQueryHandler<GetCharacterQuery, CharacterStateResult>, GetCharacterHandler>();
builder.Services.AddScoped<IQueryHandler<GetPlayerProfileQuery, GetPlayerProfileQueryResponse>, GetPlayerProfileHandler>();
builder.Services.AddScoped<ICommandHandler<HandleBattleCompletedCommand>, HandleBattleCompletedHandler>();
builder.Services.AddScoped<ICombatProfilePublisher, MassTransitCombatProfilePublisher>();

// Infrastructure (inlined from DependencyInjection.cs — composition belongs in Bootstrap)
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<ICharacterRepository, CharacterRepository>();
builder.Services.AddScoped<IInboxRepository, InboxRepository>();
builder.Services.AddScoped<ILevelingConfigProvider, LevelingConfigProvider>();

builder.Services.AddDbContext<PlayersDbContext>(options =>
{
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnection"), npgsql =>
        {
            npgsql.MigrationsHistoryTable("__ef_migrations_history", PlayersDbContext.Schema);
            npgsql.EnableRetryOnFailure();
        })
        .UseSnakeCaseNamingConvention()
        .ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>();
});

// Health checks (MassTransit contributes RabbitMQ health check automatically)
var postgresConnection = builder.Configuration.GetConnectionString("PostgresConnection")
    ?? "Host=localhost;Port=5432;Database=kombats;Username=postgres;Password=postgres";
builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnection, name: "postgresql");

// OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Kombats.Players"))
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

// Messaging (Kombats.Messaging with transactional outbox — AD-01)
builder.Services.AddMessaging<PlayersDbContext>(
    builder.Configuration,
    "players",
    configureConsumers: bus =>
    {
        bus.AddConsumer<BattleCompletedConsumer>();
    },
    configure: messagingBuilder =>
    {
        messagingBuilder.Map<PlayerCombatProfileChanged>("PlayerCombatProfileChanged");
        messagingBuilder.Map<BattleCompleted>("BattleCompleted");
    });

var app = builder.Build();

// NOTE: No Database.MigrateAsync() on startup — AD-13 forbids it.
// Migrations are applied via CI/CD pipeline.

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseApiDocumentation();

app.UseHttpsRedirection();

app.UseSerilogRequestLogging();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();

// Required for WebApplicationFactory<Program> in API tests.
public partial class Program;

