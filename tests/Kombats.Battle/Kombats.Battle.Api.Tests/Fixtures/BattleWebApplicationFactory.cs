using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace Kombats.Battle.Api.Tests.Fixtures;

public sealed class BattleWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestScheme = "Test";

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private RabbitMqContainer _rabbit = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        _rabbit = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-alpine")
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _rabbit.StartAsync());
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Override only infrastructure connection strings.
            // Rulesets, Serilog, etc. come from appsettings.json in the Bootstrap project.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgresConnection"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = GetRedisConnectionString(),
                ["Messaging:RabbitMq:Host"] = _rabbit.Hostname,
                ["Messaging:RabbitMq:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
                ["Messaging:RabbitMq:Username"] = "guest",
                ["Messaging:RabbitMq:Password"] = "guest",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace Keycloak JWT auth with a test scheme.
            // When access_token is present, authenticates with the token value as user ID.
            // When absent, returns NoResult so [Authorize] still rejects.
            services.AddAuthentication(TestScheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, null);

            // Replace Redis IConnectionMultiplexer with the Testcontainer connection.
            // This is necessary because Program.cs reads the connection string eagerly
            // at build time, before ConfigureAppConfiguration overrides apply.
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(GetRedisConnectionString()));
        });
    }

    private string GetRedisConnectionString() =>
        $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},abortConnect=false";

    internal sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // SignalR sends access tokens via query string on the negotiate/connect request
            string? token = Context.Request.Query["access_token"].FirstOrDefault();

            // Also check Authorization header for standard HTTP requests
            if (token is null
                && Context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var headerValue = authHeader.ToString();
                if (headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = headerValue["Bearer ".Length..];
            }

            if (string.IsNullOrEmpty(token))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new[]
            {
                new Claim("sub", token),
                new Claim(ClaimTypes.NameIdentifier, token),
                new Claim("preferred_username", "testuser"),
            };

            var identity = new ClaimsIdentity(claims, TestScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, TestScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

[CollectionDefinition(BattleHostCollection.Name)]
public class BattleHostCollection : ICollectionFixture<BattleWebApplicationFactory>
{
    public const string Name = "BattleHost";
}
