using System.Reflection;
using System.Text.Json.Serialization;
using FluentValidation;
using Kombats.Bff.Api.Extensions;
using Kombats.Bff.Api.Hubs;
using Kombats.Bff.Api.Middleware;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Composition;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Templates;
using Kombats.Bff.Application.Relay;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Scalar.AspNetCore;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

// Authentication & Authorization (inlined — BFF does not reference Kombats.Abstractions per AD-17)
string authority = builder.Configuration["Keycloak:Authority"]
                   ?? throw new InvalidOperationException("Keycloak:Authority configuration is required.");
string audience = builder.Configuration["Keycloak:Audience"]
                  ?? throw new InvalidOperationException("Keycloak:Audience configuration is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";

        // SignalR sends the token as a query string parameter for WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                string? accessToken = context.Request.Query["access_token"];
                string path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken)
                    && (path.StartsWith("/battlehub") || path.StartsWith("/chathub")))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Endpoints (scan Api assembly)
Assembly apiAssembly = typeof(Kombats.Bff.Api.Endpoints.IEndpoint).Assembly;
builder.Services.AddEndpoints(apiAssembly);

// Request validation
builder.Services.AddValidatorsFromAssembly(apiAssembly);

// OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Kombats.Bff"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        string? otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

// API Documentation — registers a Bearer security scheme and a global security
// requirement so Scalar's "Authorize" flow works against Keycloak-issued JWTs,
// matching the pattern used by Players/Matchmaking/Battle Api projects.
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Kombats BFF",
            Version = "v1",
            Description = "Kombats Backend-for-Frontend"
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = JwtBearerDefaults.AuthenticationScheme,
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter the JWT access token"
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document),
                []
            }
        });

        return Task.CompletedTask;
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
        else
        {
            string[]? origins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>();

            if (origins is null || origins.Length == 0)
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must be configured in non-Development environments.");
            }

            policy.WithOrigins(origins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    });
});

// Service options
builder.Services.Configure<ServicesOptions>(builder.Configuration.GetSection("Services"));

// Resilience options
ResilienceOptions resilienceOptions = builder.Configuration
    .GetSection("Resilience")
    .Get<ResilienceOptions>() ?? new ResilienceOptions();

// HttpContext accessor (required by JwtForwardingHandler)
builder.Services.AddHttpContextAccessor();

// JWT forwarding handler
builder.Services.AddTransient<JwtForwardingHandler>();

// Typed HttpClients — Players
builder.Services.AddHttpClient<IPlayersClient, PlayersClient>(client =>
{
    string baseUrl = builder.Configuration["Services:Players:BaseUrl"]
        ?? throw new InvalidOperationException("Services:Players:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<JwtForwardingHandler>()
.AddResilienceHandler("players", (pipeline, _) =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.TotalRequestTimeoutSeconds))
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = resilienceOptions.RetryMaxAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = args =>
            {
                // Only retry idempotent GET requests
                if (args.Outcome.Result?.RequestMessage?.Method != HttpMethod.Get)
                {
                    return ValueTask.FromResult(false);
                }

                return new HttpRetryStrategyOptions().ShouldHandle(args);
            }
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds),
            MinimumThroughput = resilienceOptions.CircuitBreakerFailureThreshold,
            BreakDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds)
        })
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.AttemptTimeoutSeconds));
});

// Typed HttpClients — Matchmaking
builder.Services.AddHttpClient<IMatchmakingClient, MatchmakingClient>(client =>
{
    string baseUrl = builder.Configuration["Services:Matchmaking:BaseUrl"]
        ?? throw new InvalidOperationException("Services:Matchmaking:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<JwtForwardingHandler>()
.AddResilienceHandler("matchmaking", (pipeline, _) =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.TotalRequestTimeoutSeconds))
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = resilienceOptions.RetryMaxAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = args =>
            {
                // Only retry idempotent GET requests
                if (args.Outcome.Result?.RequestMessage?.Method != HttpMethod.Get)
                {
                    return ValueTask.FromResult(false);
                }

                return new HttpRetryStrategyOptions().ShouldHandle(args);
            }
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds),
            MinimumThroughput = resilienceOptions.CircuitBreakerFailureThreshold,
            BreakDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds)
        })
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.AttemptTimeoutSeconds));
});

// Typed HttpClients — Battle
builder.Services.AddHttpClient<IBattleClient, BattleClient>(client =>
{
    string baseUrl = builder.Configuration["Services:Battle:BaseUrl"]
        ?? throw new InvalidOperationException("Services:Battle:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<JwtForwardingHandler>()
.AddResilienceHandler("battle", (pipeline, _) =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.TotalRequestTimeoutSeconds))
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = resilienceOptions.RetryMaxAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = args =>
            {
                if (args.Outcome.Result?.RequestMessage?.Method != HttpMethod.Get)
                    return ValueTask.FromResult(false);
                return new HttpRetryStrategyOptions().ShouldHandle(args);
            }
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds),
            MinimumThroughput = resilienceOptions.CircuitBreakerFailureThreshold,
            BreakDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds)
        })
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.AttemptTimeoutSeconds));
});

// Typed HttpClients — Chat (Batch 5)
builder.Services.AddHttpClient<IChatClient, ChatClient>(client =>
{
    string baseUrl = builder.Configuration["Services:Chat:BaseUrl"]
        ?? throw new InvalidOperationException("Services:Chat:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<JwtForwardingHandler>()
.AddResilienceHandler("chat", (pipeline, _) =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.TotalRequestTimeoutSeconds))
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = resilienceOptions.RetryMaxAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = args =>
            {
                if (args.Outcome.Result?.RequestMessage?.Method != HttpMethod.Get)
                    return ValueTask.FromResult(false);
                return new HttpRetryStrategyOptions().ShouldHandle(args);
            }
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds),
            MinimumThroughput = resilienceOptions.CircuitBreakerFailureThreshold,
            BreakDuration = TimeSpan.FromSeconds(resilienceOptions.CircuitBreakerBreakDurationSeconds)
        })
        .AddTimeout(TimeSpan.FromSeconds(resilienceOptions.AttemptTimeoutSeconds));
});

// SignalR — frontend-facing hub
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Frontend event sender — uses IHubContext<BattleHub> to target connections by ID.
// IHubContext is stable outside hub method scope (unlike Hub.Clients.Caller).
builder.Services.AddSingleton<IFrontendBattleSender, HubContextBattleSender>();

// Narration subsystem — pure application logic, no infrastructure dependencies
builder.Services.AddSingleton<ITemplateCatalog, InMemoryTemplateCatalog>();
builder.Services.AddSingleton<ITemplateSelector, DeterministicTemplateSelector>();
builder.Services.AddSingleton<INarrationRenderer, PlaceholderNarrationRenderer>();
builder.Services.AddSingleton<ICommentatorPolicy, DefaultCommentatorPolicy>();
builder.Services.AddSingleton<IFeedAssembler, DefaultFeedAssembler>();
builder.Services.AddSingleton<INarrationPipeline, NarrationPipeline>();

// Battle hub relay — manages per-connection downstream SignalR connections to Battle
builder.Services.AddSingleton<IBattleHubRelay, BattleHubRelay>();

// Chat hub relay — Batch 5: per-frontend-connection downstream SignalR connections to Chat
builder.Services.AddSingleton<IFrontendChatSender, HubContextChatSender>();
builder.Services.AddSingleton<IChatHubRelay, ChatHubRelay>();

// Game state composer — aggregates data from Players + Matchmaking
builder.Services.AddScoped<GameStateComposer>();

WebApplication app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.UseSerilogRequestLogging();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Global error handler for BFF exceptions
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapEndpoints();

// SignalR hub — battle realtime proxy (AD-16)
app.MapHub<BattleHub>("/battlehub");

// SignalR hub — chat realtime proxy (Batch 5)
app.MapHub<ChatHub>("/chathub");

app.Run();
