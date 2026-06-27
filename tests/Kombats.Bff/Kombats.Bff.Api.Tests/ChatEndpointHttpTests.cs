using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kombats.Bff.Api.Endpoints;
using Kombats.Bff.Api.Endpoints.Chat;
using Kombats.Bff.Api.Endpoints.PlayerCard;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Kombats.Bff.Api.Tests;

/// <summary>
/// In-process HTTP tests for the BFF chat proxy endpoints and the player-card endpoint.
/// Uses <see cref="TestServer"/> + scheme-bypass auth so we exercise the real route
/// pipeline (auth + minimal-API binding + handler) against stubbed downstream clients.
/// </summary>
public sealed class ChatEndpointHttpTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IChatClient _chat = null!;
    private IPlayersClient _players = null!;

    public async Task InitializeAsync()
    {
        _chat = Substitute.For<IChatClient>();
        _players = Substitute.For<IPlayersClient>();

        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddSingleton(_chat);
                    services.AddSingleton(_players);

                    // Only register the Batch 5 endpoints to avoid pulling in DI dependencies
                    // for unrelated endpoints (e.g. IMatchmakingClient) that aren't stubbed here.
                    services.AddTransient<IEndpoint, GetConversationsEndpoint>();
                    services.AddTransient<IEndpoint, GetConversationMessagesEndpoint>();
                    services.AddTransient<IEndpoint, GetDirectMessagesEndpoint>();
                    services.AddTransient<IEndpoint, GetOnlinePlayersEndpoint>();
                    services.AddTransient<IEndpoint, GetPlayerCardEndpoint>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        foreach (var ep in app.ApplicationServices.GetServices<IEndpoint>())
                        {
                            ep.MapEndpoint(endpoints);
                        }
                    });
                });
            })
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task GetConversations_NoAuth_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/chat/conversations");
        // Default test scheme requires the X-Test-Auth header; omit it to simulate no auth.
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConversations_WithAuth_ProxiesToChatClient_AndMapsResponse()
    {
        var convoId = Guid.NewGuid();
        _chat.GetConversationsAsync(Arg.Any<CancellationToken>())
            .Returns(new InternalConversationListResponse(new List<InternalConversationDto>
            {
                new(convoId, "Global", null, DateTimeOffset.UtcNow)
            }));

        var resp = await SendAuth(HttpMethod.Get, "/api/v1/chat/conversations");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<ConversationListResponse>();
        body.Should().NotBeNull();
        body!.Conversations.Should().HaveCount(1);
        body.Conversations[0].ConversationId.Should().Be(convoId);
        body.Conversations[0].Type.Should().Be("Global");
    }

    [Fact]
    public async Task GetConversationMessages_WithAuth_ProxiesAndMaps()
    {
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        _chat.GetMessagesAsync(conversationId, Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new InternalMessageListResponse(
                new List<InternalChatMessageDto>
                {
                    new(messageId, conversationId, new InternalChatSenderDto(senderId, "Bob"), "hi", DateTimeOffset.UtcNow)
                },
                HasMore: true));

        var resp = await SendAuth(HttpMethod.Get,
            $"/api/v1/chat/conversations/{conversationId}/messages?limit=10");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<MessageListResponse>();
        body.Should().NotBeNull();
        body!.HasMore.Should().BeTrue();
        body.Messages[0].Sender.DisplayName.Should().Be("Bob");
    }

    [Fact]
    public async Task GetConversationMessages_WhenChatReturns404_BffReturns404()
    {
        var conversationId = Guid.NewGuid();
        _chat.GetMessagesAsync(conversationId, Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((InternalMessageListResponse?)null);

        var resp = await SendAuth(HttpMethod.Get,
            $"/api/v1/chat/conversations/{conversationId}/messages");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDirectMessages_WithAuth_Proxies()
    {
        var other = Guid.NewGuid();
        _chat.GetDirectMessagesAsync(other, Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new InternalMessageListResponse(new List<InternalChatMessageDto>(), false));

        var resp = await SendAuth(HttpMethod.Get, $"/api/v1/chat/direct/{other}/messages");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOnlinePlayers_WithAuth_ProxiesAndMaps()
    {
        _chat.GetOnlinePlayersAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new InternalOnlinePlayersResponse(
                new List<InternalOnlinePlayerDto> { new(Guid.NewGuid(), "Alice") },
                TotalOnline: 1));

        var resp = await SendAuth(HttpMethod.Get, "/api/v1/chat/presence/online?limit=50&offset=0");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<OnlinePlayersResponse>();
        body.Should().NotBeNull();
        body!.Players.Should().HaveCount(1);
        body.TotalOnline.Should().Be(1);
    }

    [Fact]
    public async Task GetConversationMessages_NoAuth_Returns401()
    {
        var resp = await _client.GetAsync($"/api/v1/chat/conversations/{Guid.NewGuid()}/messages");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDirectMessages_NoAuth_Returns401()
    {
        var resp = await _client.GetAsync($"/api/v1/chat/direct/{Guid.NewGuid()}/messages");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOnlinePlayers_NoAuth_Returns401()
    {
        var resp = await _client.GetAsync("/api/v1/chat/presence/online");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPlayerCard_NoAuth_Returns401()
    {
        var resp = await _client.GetAsync($"/api/v1/players/{Guid.NewGuid()}/card");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPlayerCard_FoundProfile_MapsTo200()
    {
        var playerId = Guid.NewGuid();
        _players.GetProfileAsync(playerId, Arg.Any<CancellationToken>())
            .Returns(new InternalPlayerProfileResponse(
                playerId, "Alice", Level: 5,
                Strength: 4, Agility: 3, Intuition: 2, Vitality: 1,
                Wins: 10, Losses: 2, AvatarId: "default"));

        var resp = await SendAuth(HttpMethod.Get, $"/api/v1/players/{playerId}/card");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<PlayerCardResponse>();
        body.Should().NotBeNull();
        body!.PlayerId.Should().Be(playerId);
        body.DisplayName.Should().Be("Alice");
        body.Level.Should().Be(5);
        body.Wins.Should().Be(10);
        body.Losses.Should().Be(2);
    }

    [Fact]
    public async Task GetPlayerCard_NotFound_Maps404()
    {
        var playerId = Guid.NewGuid();
        _players.GetProfileAsync(playerId, Arg.Any<CancellationToken>())
            .Returns((InternalPlayerProfileResponse?)null);

        var resp = await SendAuth(HttpMethod.Get, $"/api/v1/players/{playerId}/card");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPlayerCard_NullDisplayName_FallsBackToUnknown()
    {
        var playerId = Guid.NewGuid();
        _players.GetProfileAsync(playerId, Arg.Any<CancellationToken>())
            .Returns(new InternalPlayerProfileResponse(
                playerId, DisplayName: null, Level: 1,
                Strength: 0, Agility: 0, Intuition: 0, Vitality: 0,
                Wins: 0, Losses: 0, AvatarId: "default"));

        var resp = await SendAuth(HttpMethod.Get, $"/api/v1/players/{playerId}/card");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PlayerCardResponse>();
        body!.DisplayName.Should().Be("Unknown");
    }

    private async Task<HttpResponseMessage> SendAuth(HttpMethod method, string path)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Add(TestAuthHandler.HeaderName, "1");
        return await _client.SendAsync(req);
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string HeaderName = "X-Test-Auth";

        public TestAuthHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(HeaderName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new System.Security.Claims.ClaimsIdentity("Test");
            identity.AddClaim(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
