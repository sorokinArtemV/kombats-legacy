using System.Security.Claims;
using System.Text.Encodings.Web;
using Kombats.Abstractions;
using Kombats.Players.Application;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.Battles;
using Kombats.Players.Application.UseCases.AllocateStatPoints;
using Kombats.Players.Application.UseCases.EnsureCharacterExists;
using Kombats.Players.Application.UseCases.GetCharacter;
using Kombats.Players.Application.UseCases.GetPlayerProfile;
using Kombats.Players.Application.UseCases.SetCharacterName;
using Kombats.Players.Domain.Entities;
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

namespace Kombats.Players.Api.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory for Players API tests. Replaces infrastructure with test
/// doubles so that API-level concerns (auth, validation, middleware) can be tested
/// without PostgreSQL, RabbitMQ, or Keycloak.
/// </summary>
public sealed class PlayersApiFactory : WebApplicationFactory<Program>
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

    internal ICommandHandler<AllocateStatPointsCommand, AllocateStatPointsResult> AllocateHandler { get; }
        = Substitute.For<ICommandHandler<AllocateStatPointsCommand, AllocateStatPointsResult>>();

    internal ICommandHandler<EnsureCharacterExistsCommand, CharacterStateResult> EnsureHandler { get; }
        = Substitute.For<ICommandHandler<EnsureCharacterExistsCommand, CharacterStateResult>>();

    internal ICommandHandler<SetCharacterNameCommand, CharacterStateResult> SetNameHandler { get; }
        = Substitute.For<ICommandHandler<SetCharacterNameCommand, CharacterStateResult>>();

    internal IQueryHandler<GetCharacterQuery, CharacterStateResult> GetCharacterHandler { get; }
        = Substitute.For<IQueryHandler<GetCharacterQuery, CharacterStateResult>>();

    internal IQueryHandler<GetPlayerProfileQuery, GetPlayerProfileQueryResponse> GetPlayerProfileHandler { get; }
        = Substitute.For<IQueryHandler<GetPlayerProfileQuery, GetPlayerProfileQueryResponse>>();

    internal ICommandHandler<HandleBattleCompletedCommand> BattleCompletedHandler { get; }
        = Substitute.For<ICommandHandler<HandleBattleCompletedCommand>>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        // Provide CORS origins for non-Development environments
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

            // Replace application handlers with stubs
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<AllocateStatPointsCommand, AllocateStatPointsResult>>(_ => AllocateHandler));
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<EnsureCharacterExistsCommand, CharacterStateResult>>(_ => EnsureHandler));
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<SetCharacterNameCommand, CharacterStateResult>>(_ => SetNameHandler));
            services.Replace(ServiceDescriptor.Scoped<IQueryHandler<GetCharacterQuery, CharacterStateResult>>(_ => GetCharacterHandler));
            services.Replace(ServiceDescriptor.Scoped<IQueryHandler<GetPlayerProfileQuery, GetPlayerProfileQueryResponse>>(_ => GetPlayerProfileHandler));
            services.Replace(ServiceDescriptor.Scoped<ICommandHandler<HandleBattleCompletedCommand>>(_ => BattleCompletedHandler));

            // Replace infrastructure ports with no-ops
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<IUnitOfWork>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<ICharacterRepository>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<IInboxRepository>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<ICombatProfilePublisher>()));
            services.Replace(ServiceDescriptor.Scoped(_ => Substitute.For<ILevelingConfigProvider>()));
        });
    }

    internal sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PlayersApiFactory factory)
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
