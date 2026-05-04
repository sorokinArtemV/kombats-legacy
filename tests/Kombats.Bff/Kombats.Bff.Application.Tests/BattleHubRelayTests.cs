using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Templates;
using Kombats.Bff.Application.Relay;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kombats.Bff.Application.Tests;

public sealed class BattleHubRelayTests
{
    private readonly BattleHubRelay _relay;
    private readonly IFrontendBattleSender _sender;
    private readonly INarrationPipeline _pipeline;

    private static ServicesOptions CreateServicesOptions(string battleBaseUrl = "http://localhost:5003") => new()
    {
        Players = new ServiceOptions { BaseUrl = "http://localhost:5001" },
        Matchmaking = new ServiceOptions { BaseUrl = "http://localhost:5002" },
        Battle = new ServiceOptions { BaseUrl = battleBaseUrl }
    };

    private static INarrationPipeline CreateRealPipeline() => new NarrationPipeline(
        new InMemoryTemplateCatalog(),
        new DeterministicTemplateSelector(),
        new PlaceholderNarrationRenderer(),
        new DefaultCommentatorPolicy(),
        new DefaultFeedAssembler());

    public BattleHubRelayTests()
    {
        _sender = Substitute.For<IFrontendBattleSender>();
        _pipeline = CreateRealPipeline();

        _relay = new BattleHubRelay(
            Options.Create(CreateServicesOptions()),
            _sender,
            _pipeline,
            Substitute.For<ILogger<BattleHubRelay>>());
    }

    [Fact]
    public async Task SubmitTurnActionAsync_WithoutJoin_ThrowsInvalidOperation()
    {
        Func<Task> act = () => _relay.SubmitTurnActionAsync(
            "connection-1",
            Guid.NewGuid(),
            0,
            "Attack:Head");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active battle connection*");
    }

    [Fact]
    public async Task DisconnectAsync_WithoutConnection_DoesNotThrow()
    {
        Func<Task> act = () => _relay.DisconnectAsync("nonexistent-connection");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task JoinBattleAsync_WithUnreachableBattle_ThrowsAndCleansUp()
    {
        var relay = new BattleHubRelay(
            Options.Create(CreateServicesOptions("http://unreachable-host:9999")),
            Substitute.For<IFrontendBattleSender>(),
            _pipeline,
            Substitute.For<ILogger<BattleHubRelay>>());

        Func<Task> act = () => relay.JoinBattleAsync(
            Guid.NewGuid(),
            "connection-1",
            "fake-jwt-token");

        await act.Should().ThrowAsync<Exception>();

        // After failure, SubmitTurnAction should also fail (connection cleaned up)
        Func<Task> submitAct = () => relay.SubmitTurnActionAsync(
            "connection-1", Guid.NewGuid(), 0, "Attack:Head");
        await submitAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpAllConnections()
    {
        Func<Task> act = async () => await _relay.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void BattleHubRelay_ImplementsIBattleHubRelay()
    {
        _relay.Should().BeAssignableTo<IBattleHubRelay>();
    }

    [Fact]
    public void BattleHubRelay_ImplementsIAsyncDisposable()
    {
        _relay.Should().BeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public void BattleHubRelay_UsesIFrontendBattleSender_NotCallback()
    {
        var ctors = typeof(BattleHubRelay).GetConstructors();
        ctors.Should().HaveCount(1);

        var parameters = ctors[0].GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IFrontendBattleSender),
            "BattleHubRelay must use IFrontendBattleSender for stable connection targeting");
    }

    [Fact]
    public void IBattleHubRelay_JoinBattleAsync_DoesNotAcceptCallback()
    {
        var method = typeof(IBattleHubRelay).GetMethod("JoinBattleAsync");
        method.Should().NotBeNull();

        var parameters = method!.GetParameters();
        parameters.Should().NotContain(p => p.ParameterType.Name.StartsWith("Func"),
            "JoinBattleAsync must not accept a callback — events are sent via IFrontendBattleSender");
    }

    [Fact]
    public void BattleHubRelay_RequiresINarrationPipeline()
    {
        var ctors = typeof(BattleHubRelay).GetConstructors();
        var parameters = ctors[0].GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(INarrationPipeline),
            "BattleHubRelay must accept INarrationPipeline for feed generation");
    }

    [Fact]
    public void BattleFeedUpdatedEvent_IsCorrectName()
    {
        BattleHubRelay.BattleFeedUpdatedEvent.Should().Be("BattleFeedUpdated");
    }
}
