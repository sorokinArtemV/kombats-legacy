using System.Security.Claims;
using System.Text.Encodings.Web;
using Kombats.Abstractions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.GetConversations;
using Kombats.Chat.Application.UseCases.GetDirectMessages;
using Kombats.Chat.Application.UseCases.GetOnlinePlayers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Kombats.Chat.Api.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory for Chat API tests. Replaces infrastructure with test
/// doubles so that API-level concerns (auth, validation, middleware) can be tested
/// without PostgreSQL, RabbitMQ, Redis, or Keycloak.
/// </summary>
public sealed class ChatApiFactory : WebApplicationFactory<Program>
{
    public const string TestScheme = "Test";
    public const string TestSubjectId = "d290f1ee-6c54-4b01-90e6-d701748f0851";

    /// <summary>
    /// When true, the test auth handler will authenticate the request.
    /// Set to false to simulate unauthenticated requests.
    /// </summary>
    public bool AuthenticateRequests { get; set; } = true;

    /// <summary>
    /// Override the hosting environment name. Defaults to Development.
    /// </summary>
    public string EnvironmentName { get; set; } = "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        if (EnvironmentName != "Development")
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = "https://test.example.com"
                });
            });
        }

        builder.ConfigureTestServices(services =>
        {
            // Replace auth with test scheme
            services.AddAuthentication(TestScheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, null);

            // Pass factory reference so auth handler can read AuthenticateRequests
            services.AddSingleton(this);

            // Remove all infrastructure and handler registrations from Bootstrap,
            // then replace with stubs. This avoids DI validation failures from
            // real registrations that depend on ChatDbContext/Postgres.
            // Remove Batch 1 + Batch 2 real registrations
            RemoveServicesByType(services, "ChatDbContext");
            RemoveServicesByType(services, "IConversationRepository");
            RemoveServicesByType(services, "IMessageRepository");
            RemoveServicesByType(services, "GetConversationMessagesHandler");
            RemoveServicesByType(services, "GetConversationsHandler");
            RemoveServicesByType(services, "GetDirectMessagesHandler");
            RemoveServicesByType(services, "IConnectionMultiplexer");
            RemoveServicesByType(services, "ConnectionMultiplexer");
            RemoveServicesByType(services, "IPresenceStore");
            RemoveServicesByType(services, "IRateLimiter");
            RemoveServicesByType(services, "IPlayerInfoCache");
            RemoveServicesByType(services, "IDisplayNameResolver");
            RemoveServicesByType(services, "IEligibilityChecker");
            RemoveServicesByType(services, "GetOnlinePlayersHandler");
            RemoveServicesByType(services, "RedisPresenceStore");
            RemoveServicesByType(services, "RedisRateLimiter");
            RemoveServicesByType(services, "RedisPlayerInfoCache");
            RemoveServicesByType(services, "DisplayNameResolver");
            RemoveServicesByType(services, "EligibilityChecker");

            // Stub Batch 1 handlers (API tests verify transport, not business logic)
            var getConversationsHandler = Substitute.For<IQueryHandler<GetConversationsQuery, GetConversationsResponse>>();
            getConversationsHandler
                .HandleAsync(Arg.Any<GetConversationsQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(new GetConversationsResponse(new List<ConversationDto>())));
            services.AddScoped(_ => getConversationsHandler);

            var getMessagesHandler = Substitute.For<IQueryHandler<GetConversationMessagesQuery, GetConversationMessagesResponse>>();
            getMessagesHandler
                .HandleAsync(Arg.Any<GetConversationMessagesQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Failure<GetConversationMessagesResponse>(
                    Error.NotFound("GetConversationMessages.NotFound", "Conversation not found.")));
            services.AddScoped(_ => getMessagesHandler);

            var getDirectMessagesHandler = Substitute.For<IQueryHandler<GetDirectMessagesQuery, GetConversationMessagesResponse>>();
            getDirectMessagesHandler
                .HandleAsync(
                    Arg.Is<GetDirectMessagesQuery>(q => q.CallerIdentityId == q.OtherIdentityId),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Failure<GetConversationMessagesResponse>(
                    Error.Validation("GetDirectMessages.SameUser", "Cannot query direct messages with yourself.")));
            getDirectMessagesHandler
                .HandleAsync(
                    Arg.Is<GetDirectMessagesQuery>(q => q.CallerIdentityId != q.OtherIdentityId),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success(new GetConversationMessagesResponse(new List<MessageDto>(), false)));
            services.AddScoped(_ => getDirectMessagesHandler);

            // Stub repositories
            services.AddScoped(_ => Substitute.For<IConversationRepository>());
            services.AddScoped(_ => Substitute.For<IMessageRepository>());

            // Stub Batch 2 handlers
            var getOnlinePlayersHandler = Substitute.For<IQueryHandler<GetOnlinePlayersQuery, GetOnlinePlayersResponse>>();
            getOnlinePlayersHandler
                .HandleAsync(Arg.Any<GetOnlinePlayersQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(new GetOnlinePlayersResponse(new List<OnlinePlayerDto>(), 0)));
            services.AddScoped(_ => getOnlinePlayersHandler);

            // Stub Batch 2 ports
            services.AddScoped(_ => Substitute.For<IPresenceStore>());
            services.AddScoped(_ => Substitute.For<IRateLimiter>());
            services.AddScoped(_ => Substitute.For<IPlayerInfoCache>());
            services.AddScoped(_ => Substitute.For<IDisplayNameResolver>());
            services.AddScoped(_ => Substitute.For<IEligibilityChecker>());
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
        ChatApiFactory factory)
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
                new Claim("sub", TestSubjectId),
                new Claim("preferred_username", "testuser"),
                new Claim("email", "test@example.com")
            };

            var identity = new ClaimsIdentity(claims, TestScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, TestScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
