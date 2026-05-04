using System.Security.Claims;
using System.Text.Encodings.Web;
using Kombats.Abstractions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;
using Kombats.Matchmaking.Application.UseCases.GetQueueStatus;
using Kombats.Matchmaking.Application.UseCases.JoinQueue;
using Kombats.Matchmaking.Application.UseCases.LeaveQueue;
using Kombats.Matchmaking.Application.UseCases.TimeoutStaleMatches;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Kombats.Matchmaking.Api.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory for Matchmaking API tests. Replaces infrastructure with test
/// doubles so that API-level concerns (auth, validation, middleware, routing) can be tested
/// without PostgreSQL, Redis, RabbitMQ, or Keycloak.
/// </summary>
public sealed class MatchmakingApiFactory : WebApplicationFactory<Program>
{
    public const string TestScheme = "Test";
    public const string TestSubjectId = "d290f1ee-6c54-4b01-90e6-d701748f0851";

    public bool AuthenticateRequests { get; set; } = true;

    internal ICommandHandler<JoinQueueCommand, JoinQueueResult> JoinQueueHandler { get; }
        = Substitute.For<ICommandHandler<JoinQueueCommand, JoinQueueResult>>();

    internal ICommandHandler<LeaveQueueCommand, LeaveQueueResult> LeaveQueueHandler { get; }
        = Substitute.For<ICommandHandler<LeaveQueueCommand, LeaveQueueResult>>();

    internal IQueryHandler<GetQueueStatusQuery, QueueStatusResult> GetQueueStatusHandler { get; }
        = Substitute.For<IQueryHandler<GetQueueStatusQuery, QueueStatusResult>>();

    internal ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult> TickHandler { get; }
        = Substitute.For<ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>>();

    internal ICommandHandler<TimeoutStaleMatchesCommand, int> TimeoutHandler { get; }
        = Substitute.For<ICommandHandler<TimeoutStaleMatchesCommand, int>>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Replace auth with test scheme
            services.AddAuthentication(TestScheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, null);
            services.AddSingleton(this);

            // Replace application handlers with stubs
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<JoinQueueCommand, JoinQueueResult>>(_ => JoinQueueHandler));
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<LeaveQueueCommand, LeaveQueueResult>>(_ => LeaveQueueHandler));
            services.Replace(ServiceDescriptor.Scoped<IQueryHandler<GetQueueStatusQuery, QueueStatusResult>>(_ => GetQueueStatusHandler));
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>>(_ => TickHandler));
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<TimeoutStaleMatchesCommand, int>>(_ => TimeoutHandler));

            // Replace infrastructure ports with no-ops
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<IMatchRepository>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<IMatchQueueStore>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<IPlayerMatchStatusStore>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<IPlayerCombatProfileRepository>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<IUnitOfWork>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<ICreateBattlePublisher>()));
        });
    }

    internal sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        MatchmakingApiFactory factory)
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
