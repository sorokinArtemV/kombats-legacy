using FluentAssertions;
using Kombats.Battle.Infrastructure.Configuration;
using Kombats.Battle.Infrastructure.Realtime.SignalR;
using Kombats.Battle.Realtime.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests.Realtime;

public class SignalRBattleRealtimeNotifierTests
{
    private const int WinXp = 10;
    private const int LossXp = 5;

    private readonly IHubContext<BattleHub> _hubContext = Substitute.For<IHubContext<BattleHub>>();
    private readonly IHubClients _clients = Substitute.For<IHubClients>();
    private readonly IClientProxy _groupProxy = Substitute.For<IClientProxy>();
    private readonly SignalRBattleRealtimeNotifier _notifier;

    private readonly Guid _battleId = Guid.NewGuid();
    private readonly Guid _winnerId = Guid.NewGuid();
    private readonly DateTimeOffset _endedAt = DateTimeOffset.UtcNow;

    public SignalRBattleRealtimeNotifierTests()
    {
        _hubContext.Clients.Returns(_clients);
        _clients.Group(Arg.Any<string>()).Returns(_groupProxy);

        var options = Options.Create(new BattleRewardsOptions { WinXp = WinXp, LossXp = LossXp });

        _notifier = new SignalRBattleRealtimeNotifier(
            _hubContext,
            NullLogger<SignalRBattleRealtimeNotifier>.Instance,
            options);
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Timeout")]
    [InlineData("Cancelled")]
    [InlineData("AdminForced")]
    [InlineData("SystemError")]
    public async Task NotifyBattleEnded_WithWinner_PopulatesXp(string reason)
    {
        await _notifier.NotifyBattleEndedAsync(_battleId, reason, _winnerId, _endedAt);

        var payload = CapturePayload();

        payload.WinnerXp.Should().Be(WinXp);
        payload.LoserXp.Should().Be(LossXp);
        payload.WinnerPlayerId.Should().Be(_winnerId);
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("DoubleForfeit")]
    [InlineData("Timeout")]
    [InlineData("Cancelled")]
    [InlineData("AdminForced")]
    [InlineData("SystemError")]
    public async Task NotifyBattleEnded_NoWinner_LeavesXpNull(string reason)
    {
        await _notifier.NotifyBattleEndedAsync(_battleId, reason, winnerPlayerId: null, _endedAt);

        var payload = CapturePayload();

        payload.WinnerXp.Should().BeNull();
        payload.LoserXp.Should().BeNull();
        payload.WinnerPlayerId.Should().BeNull();
    }

    [Fact]
    public async Task NotifyBattleEnded_BroadcastsToBattleGroup()
    {
        await _notifier.NotifyBattleEndedAsync(_battleId, "Normal", _winnerId, _endedAt);

        _clients.Received(1).Group($"battle:{_battleId}");
    }

    [Fact]
    public async Task NotifyBattleEnded_PreservesBattleIdReasonEndedAt()
    {
        await _notifier.NotifyBattleEndedAsync(_battleId, "Normal", _winnerId, _endedAt);

        var payload = CapturePayload();

        payload.BattleId.Should().Be(_battleId);
        payload.Reason.Should().Be(BattleEndReasonRealtime.Normal);
        payload.EndedAt.Should().Be(_endedAt);
    }

    [Fact]
    public async Task NotifyBattleEnded_UnknownReason_FallsBackToUnknown()
    {
        await _notifier.NotifyBattleEndedAsync(_battleId, "NotAValidReason", _winnerId, _endedAt);

        var payload = CapturePayload();

        payload.Reason.Should().Be(BattleEndReasonRealtime.Unknown);
        // XP rule is reason-independent: winner present → XP populated even on Unknown.
        payload.WinnerXp.Should().Be(WinXp);
        payload.LoserXp.Should().Be(LossXp);
    }

    private BattleEndedRealtime CapturePayload()
    {
        var calls = _groupProxy.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IClientProxy.SendCoreAsync))
            .ToList();

        calls.Should().HaveCount(1, "exactly one BattleEnded broadcast is expected");

        var args = calls[0].GetArguments();
        args[0].Should().Be(RealtimeEventNames.BattleEnded);

        var payloadArgs = args[1] as object?[];
        payloadArgs.Should().NotBeNull();
        payloadArgs!.Should().HaveCount(1);

        return (BattleEndedRealtime)payloadArgs[0]!;
    }
}
