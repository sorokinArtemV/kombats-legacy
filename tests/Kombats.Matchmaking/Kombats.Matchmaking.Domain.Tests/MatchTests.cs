using FluentAssertions;
using Kombats.Matchmaking.Domain;
using Xunit;

namespace Kombats.Matchmaking.Domain.Tests;

public sealed class MatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(1);

    private static Match CreateMatch() =>
        Match.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "default",
            Now);

    // --- Factory ---

    [Fact]
    public void Create_ValidInputs_ReturnsQueuedMatch()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        var match = Match.Create(matchId, battleId, playerA, playerB, "ranked", Now);

        match.MatchId.Should().Be(matchId);
        match.BattleId.Should().Be(battleId);
        match.PlayerAId.Should().Be(playerA);
        match.PlayerBId.Should().Be(playerB);
        match.Variant.Should().Be("ranked");
        match.State.Should().Be(MatchState.Queued);
        match.CreatedAtUtc.Should().Be(Now);
        match.UpdatedAtUtc.Should().Be(Now);
        match.IsActive.Should().BeTrue();
        match.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Create_EmptyMatchId_Throws()
    {
        var act = () => Match.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "default", Now);
        act.Should().Throw<ArgumentException>().WithParameterName("matchId");
    }

    [Fact]
    public void Create_EmptyBattleId_Throws()
    {
        var act = () => Match.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "default", Now);
        act.Should().Throw<ArgumentException>().WithParameterName("battleId");
    }

    [Fact]
    public void Create_EmptyPlayerAId_Throws()
    {
        var act = () => Match.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "default", Now);
        act.Should().Throw<ArgumentException>().WithParameterName("playerAId");
    }

    [Fact]
    public void Create_EmptyPlayerBId_Throws()
    {
        var act = () => Match.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "default", Now);
        act.Should().Throw<ArgumentException>().WithParameterName("playerBId");
    }

    [Fact]
    public void Create_SamePlayerAAndB_Throws()
    {
        var id = Guid.NewGuid();
        var act = () => Match.Create(Guid.NewGuid(), Guid.NewGuid(), id, id, "default", Now);
        act.Should().Throw<ArgumentException>().WithParameterName("playerBId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_InvalidVariant_Throws(string? variant)
    {
        var act = () => Match.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), variant!, Now);
        act.Should().Throw<ArgumentException>().WithParameterName("variant");
    }

    // --- Rehydrate ---

    [Fact]
    public void Rehydrate_RestoresAllFields()
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", MatchState.BattleCreated, Now, Later);

        match.State.Should().Be(MatchState.BattleCreated);
        match.CreatedAtUtc.Should().Be(Now);
        match.UpdatedAtUtc.Should().Be(Later);
    }

    // --- Happy Path: Full Lifecycle ---

    [Fact]
    public void FullLifecycle_Queued_To_Completed()
    {
        var match = CreateMatch();

        match.State.Should().Be(MatchState.Queued);

        match.MarkBattleCreateRequested(Later);
        match.State.Should().Be(MatchState.BattleCreateRequested);

        match.TryMarkBattleCreated(Later.AddMinutes(1)).Should().BeTrue();
        match.State.Should().Be(MatchState.BattleCreated);

        match.TryMarkCompleted(Later.AddMinutes(2)).Should().BeTrue();
        match.State.Should().Be(MatchState.Completed);
        match.IsTerminal.Should().BeTrue();
    }

    // --- MarkBattleCreateRequested ---

    [Fact]
    public void MarkBattleCreateRequested_FromQueued_Succeeds()
    {
        var match = CreateMatch();
        match.MarkBattleCreateRequested(Later);
        match.State.Should().Be(MatchState.BattleCreateRequested);
        match.UpdatedAtUtc.Should().Be(Later);
    }

    [Theory]
    [InlineData(MatchState.BattleCreateRequested)]
    [InlineData(MatchState.BattleCreated)]
    [InlineData(MatchState.Completed)]
    [InlineData(MatchState.TimedOut)]
    [InlineData(MatchState.Cancelled)]
    public void MarkBattleCreateRequested_FromNonQueued_Throws(MatchState state)
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", state, Now, Now);

        var act = () => match.MarkBattleCreateRequested(Later);
        act.Should().Throw<InvalidOperationException>();
    }

    // --- TryMarkBattleCreated ---

    [Fact]
    public void TryMarkBattleCreated_FromBattleCreateRequested_ReturnsTrue()
    {
        var match = CreateMatch();
        match.MarkBattleCreateRequested(Later);

        match.TryMarkBattleCreated(Later.AddMinutes(1)).Should().BeTrue();
        match.State.Should().Be(MatchState.BattleCreated);
    }

    [Theory]
    [InlineData(MatchState.Queued)]
    [InlineData(MatchState.BattleCreated)]
    [InlineData(MatchState.Completed)]
    [InlineData(MatchState.TimedOut)]
    [InlineData(MatchState.Cancelled)]
    public void TryMarkBattleCreated_FromNonBattleCreateRequested_ReturnsFalse(MatchState state)
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", state, Now, Now);

        match.TryMarkBattleCreated(Later).Should().BeFalse();
    }

    // --- TryMarkCompleted ---

    [Fact]
    public void TryMarkCompleted_FromBattleCreated_ReturnsTrue()
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", MatchState.BattleCreated, Now, Now);

        match.TryMarkCompleted(Later).Should().BeTrue();
        match.State.Should().Be(MatchState.Completed);
    }

    [Theory]
    [InlineData(MatchState.Queued)]
    [InlineData(MatchState.BattleCreateRequested)]
    [InlineData(MatchState.Completed)]
    [InlineData(MatchState.TimedOut)]
    [InlineData(MatchState.Cancelled)]
    public void TryMarkCompleted_FromNonBattleCreated_ReturnsFalse(MatchState state)
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", state, Now, Now);

        match.TryMarkCompleted(Later).Should().BeFalse();
    }

    // --- TryMarkTimedOut ---

    [Fact]
    public void TryMarkTimedOut_FromBattleCreateRequested_ReturnsTrue()
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", MatchState.BattleCreateRequested, Now, Now);

        match.TryMarkTimedOut(Later).Should().BeTrue();
        match.State.Should().Be(MatchState.TimedOut);
    }

    [Fact]
    public void TryMarkTimedOut_FromBattleCreated_ReturnsTrue()
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", MatchState.BattleCreated, Now, Now);

        match.TryMarkTimedOut(Later).Should().BeTrue();
        match.State.Should().Be(MatchState.TimedOut);
    }

    [Theory]
    [InlineData(MatchState.Queued)]
    [InlineData(MatchState.Completed)]
    [InlineData(MatchState.TimedOut)]
    [InlineData(MatchState.Cancelled)]
    public void TryMarkTimedOut_FromInvalidState_ReturnsFalse(MatchState state)
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", state, Now, Now);

        match.TryMarkTimedOut(Later).Should().BeFalse();
    }

    // --- TryCancel ---

    [Theory]
    [InlineData(MatchState.Queued)]
    [InlineData(MatchState.BattleCreateRequested)]
    [InlineData(MatchState.BattleCreated)]
    public void TryCancel_FromActiveState_ReturnsTrue(MatchState state)
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", state, Now, Now);

        match.TryCancel(Later).Should().BeTrue();
        match.State.Should().Be(MatchState.Cancelled);
    }

    [Theory]
    [InlineData(MatchState.Completed)]
    [InlineData(MatchState.TimedOut)]
    [InlineData(MatchState.Cancelled)]
    public void TryCancel_FromTerminalState_ReturnsFalse(MatchState state)
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", state, Now, Now);

        match.TryCancel(Later).Should().BeFalse();
    }

    // --- IsTerminal / IsActive ---

    [Theory]
    [InlineData(MatchState.Completed, true)]
    [InlineData(MatchState.TimedOut, true)]
    [InlineData(MatchState.Cancelled, true)]
    [InlineData(MatchState.Queued, false)]
    [InlineData(MatchState.BattleCreateRequested, false)]
    [InlineData(MatchState.BattleCreated, false)]
    public void IsTerminal_CorrectForAllStates(MatchState state, bool expected)
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", state, Now, Now);

        match.IsTerminal.Should().Be(expected);
        match.IsActive.Should().Be(!expected);
    }

    // --- InvolvesPlayer ---

    [Fact]
    public void InvolvesPlayer_PlayerA_ReturnsTrue()
    {
        var playerA = Guid.NewGuid();
        var match = Match.Create(Guid.NewGuid(), Guid.NewGuid(), playerA, Guid.NewGuid(), "default", Now);
        match.InvolvesPlayer(playerA).Should().BeTrue();
    }

    [Fact]
    public void InvolvesPlayer_PlayerB_ReturnsTrue()
    {
        var playerB = Guid.NewGuid();
        var match = Match.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), playerB, "default", Now);
        match.InvolvesPlayer(playerB).Should().BeTrue();
    }

    [Fact]
    public void InvolvesPlayer_UnrelatedPlayer_ReturnsFalse()
    {
        var match = CreateMatch();
        match.InvolvesPlayer(Guid.NewGuid()).Should().BeFalse();
    }

    // --- Idempotency: double-call on CAS methods ---

    [Fact]
    public void TryMarkBattleCreated_CalledTwice_SecondReturnsFalse()
    {
        var match = CreateMatch();
        match.MarkBattleCreateRequested(Later);
        match.TryMarkBattleCreated(Later.AddMinutes(1)).Should().BeTrue();
        match.TryMarkBattleCreated(Later.AddMinutes(2)).Should().BeFalse();
    }

    [Fact]
    public void TryMarkCompleted_CalledTwice_SecondReturnsFalse()
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", MatchState.BattleCreated, Now, Now);
        match.TryMarkCompleted(Later).Should().BeTrue();
        match.TryMarkCompleted(Later.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void TryMarkTimedOut_CalledTwice_SecondReturnsFalse()
    {
        var match = Match.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "default", MatchState.BattleCreateRequested, Now, Now);
        match.TryMarkTimedOut(Later).Should().BeTrue();
        match.TryMarkTimedOut(Later.AddMinutes(1)).Should().BeFalse();
    }

    // --- UpdatedAtUtc is set on transition ---

    [Fact]
    public void MarkBattleCreateRequested_UpdatesTimestamp()
    {
        var match = CreateMatch();
        match.MarkBattleCreateRequested(Later);
        match.UpdatedAtUtc.Should().Be(Later);
    }

    [Fact]
    public void TryMarkBattleCreated_UpdatesTimestamp()
    {
        var match = CreateMatch();
        match.MarkBattleCreateRequested(Later);
        var t = Later.AddMinutes(5);
        match.TryMarkBattleCreated(t);
        match.UpdatedAtUtc.Should().Be(t);
    }
}
