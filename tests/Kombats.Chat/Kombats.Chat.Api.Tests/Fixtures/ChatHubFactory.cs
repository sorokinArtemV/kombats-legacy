using System.Security.Claims;
using System.Text.Encodings.Web;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Kombats.Chat.Api.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory tuned for hub integration tests. Keeps the real Batch 3
/// SignalR hub, command handlers, notifier, message filter, and user restriction
/// (these are the units under test). Replaces only the underlying infrastructure
/// ports (presence, rate limiter, eligibility, repositories, display-name resolver)
/// with NSubstitute substitutes that tests can configure.
/// </summary>
public sealed class ChatHubFactory : WebApplicationFactory<Program>
{
    public const string TestScheme = "Test";

    public Guid CallerIdentityId { get; set; } = Guid.Parse("d290f1ee-6c54-4b01-90e6-d701748f0851");
    public bool AuthenticateRequests { get; set; } = true;

    internal IPresenceStore Presence { get; } = Substitute.For<IPresenceStore>();
    internal IRateLimiter RateLimiter { get; } = Substitute.For<IRateLimiter>();
    internal IEligibilityChecker Eligibility { get; } = Substitute.For<IEligibilityChecker>();
    internal IDisplayNameResolver DisplayNames { get; } = Substitute.For<IDisplayNameResolver>();
    internal IPlayerInfoCache PlayerInfo { get; } = Substitute.For<IPlayerInfoCache>();
    internal IConversationRepository Conversations { get; } = Substitute.For<IConversationRepository>();
    internal IMessageRepository Messages { get; } = Substitute.For<IMessageRepository>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestScheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, null);

            services.AddSingleton(this);

            // Remove DbContext + real repositories + Redis + resolvers + caches.
            RemoveServicesByType(services, "ChatDbContext");
            RemoveServicesByType(services, "IConversationRepository");
            RemoveServicesByType(services, "IMessageRepository");
            RemoveServicesByType(services, "IConnectionMultiplexer");
            RemoveServicesByType(services, "ConnectionMultiplexer");
            RemoveServicesByType(services, "IPresenceStore");
            RemoveServicesByType(services, "IRateLimiter");
            RemoveServicesByType(services, "IPlayerInfoCache");
            RemoveServicesByType(services, "IDisplayNameResolver");
            RemoveServicesByType(services, "IEligibilityChecker");
            RemoveServicesByType(services, "RedisPresenceStore");
            RemoveServicesByType(services, "RedisRateLimiter");
            RemoveServicesByType(services, "RedisPlayerInfoCache");
            RemoveServicesByType(services, "DisplayNameResolver");
            RemoveServicesByType(services, "EligibilityChecker");

            services.AddScoped(_ => Conversations);
            services.AddScoped(_ => Messages);
            services.AddScoped(_ => Presence);
            services.AddScoped(_ => RateLimiter);
            services.AddScoped(_ => PlayerInfo);
            services.AddScoped(_ => DisplayNames);
            services.AddScoped(_ => Eligibility);
        });
    }

    private static void RemoveServicesByType(IServiceCollection services, string typeName)
    {
        var descriptors = services
            .Where(d =>
                d.ServiceType.FullName?.Contains(typeName) == true
                || d.ImplementationType?.FullName?.Contains(typeName) == true
                || d.ServiceType.Name.Contains(typeName)
                || (d.ImplementationType?.Name.Contains(typeName) ?? false))
            .ToList();
        foreach (var d in descriptors)
            services.Remove(d);
    }

    internal sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ChatHubFactory factory)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!factory.AuthenticateRequests)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, factory.CallerIdentityId.ToString()),
                new Claim("preferred_username", "testuser")
            };
            var identity = new ClaimsIdentity(claims, TestScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, TestScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
