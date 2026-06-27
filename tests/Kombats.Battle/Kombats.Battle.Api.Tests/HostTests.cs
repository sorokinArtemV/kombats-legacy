using System.Net;
using FluentAssertions;
using Kombats.Battle.Api.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Kombats.Battle.Api.Tests;

[Collection(BattleHostCollection.Name)]
public class HostTests
{
    private readonly BattleWebApplicationFactory _factory;

    public HostTests(BattleWebApplicationFactory factory) => _factory = factory;

    // ========== Health endpoints ==========

    [Fact]
    public async Task HealthLive_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_Returns200Or503()
    {
        // Ready check probes Postgres + Redis — may be 503 if containers are slow,
        // but should not return 404 or throw
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task HealthLive_AllowsAnonymous()
    {
        // Health endpoints must not require auth
        var client = _factory.CreateClient();
        // No Authorization header
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ========== SignalR hub auth enforcement ==========

    [Fact]
    public async Task BattleHub_NoAuth_ConnectionRefused()
    {
        var connection = CreateHubConnection(authenticated: false);

        // Connecting without auth should fail
        var act = () => connection.StartAsync();
        await act.Should().ThrowAsync<Exception>();

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task BattleHub_WithAuth_ConnectionSucceeds()
    {
        var connection = CreateHubConnection(authenticated: true);

        await connection.StartAsync();
        connection.State.Should().Be(HubConnectionState.Connected);

        await connection.DisposeAsync();
    }

    // ========== SignalR hub method behavior ==========

    [Fact]
    public async Task JoinBattle_NonExistentBattle_ThrowsHubException()
    {
        var connection = CreateHubConnection(authenticated: true);
        await connection.StartAsync();

        // Invoke JoinBattle for a battle that doesn't exist in Redis.
        // The hub should propagate a HubException ("Battle {id} not found").
        var act = () => connection.InvokeAsync<object>("JoinBattle", Guid.NewGuid());
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*not found*");

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SubmitTurnAction_NonExistentBattle_ThrowsHubException()
    {
        var connection = CreateHubConnection(authenticated: true);
        await connection.StartAsync();

        // Invoke SubmitTurnAction for a battle that doesn't exist.
        // The hub wraps InvalidOperationException in HubException.
        var act = () => connection.InvokeAsync("SubmitTurnAction", Guid.NewGuid(), 1, "{}");
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*not found*");

        await connection.DisposeAsync();
    }

    // ========== Helpers ==========

    private HubConnection CreateHubConnection(bool authenticated, string? userId = null)
    {
        userId ??= Guid.NewGuid().ToString();
        var hubUrl = _factory.Server.BaseAddress + "battlehub";

        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                if (authenticated)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(userId);
                }
            });

        return builder.Build();
    }
}
