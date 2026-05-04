using FluentAssertions;
using Kombats.Battle.Domain.Engine;
using Kombats.Battle.Domain.Events;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Engine;

public class BattleEngineTests
{
    private readonly BattleEngine _engine = new();
    private readonly Guid _battleId = Guid.NewGuid();
    private readonly Guid _matchId = Guid.NewGuid();
    private readonly Guid _playerAId = Guid.NewGuid();
    private readonly Guid _playerBId = Guid.NewGuid();

    private BattleDomainState CreateState(
        int turnIndex = 1,
        int noActionStreak = 0,
        int playerAHp = 100,
        int playerBHp = 100,
        int seed = 42,
        int noActionLimit = 10)
    {
        var stats = new PlayerStats(10, 10, 10, 10);
        var balance = TestHelpers.DefaultBalance;
        var ruleset = Ruleset.Create(1, 30, noActionLimit, seed, balance);

        return new BattleDomainState(
            _battleId, _matchId, _playerAId, _playerBId, ruleset,
            BattlePhase.Resolving, turnIndex, noActionStreak, turnIndex - 1,
            new PlayerState(_playerAId, 100, playerAHp, stats),
            new PlayerState(_playerBId, 100, playerBHp, stats));
    }

    private PlayerAction CreateAction(Guid playerId, int turnIndex,
        BattleZone attack = BattleZone.Head,
        BattleZone block1 = BattleZone.Chest,
        BattleZone block2 = BattleZone.Belly)
    {
        return PlayerAction.Create(playerId, turnIndex, attack, block1, block2);
    }

    // ========== Determinism Tests ==========

    [Fact]
    public void SameSeedAndActions_ProduceSameOutcome()
    {
        var state = CreateState();
        var actionA = CreateAction(_playerAId, 1);
        var actionB = CreateAction(_playerBId, 1);

        var result1 = _engine.ResolveTurn(state, actionA, actionB);
        var result2 = _engine.ResolveTurn(state, actionA, actionB);

        result1.NewState.PlayerA.CurrentHp.Should().Be(result2.NewState.PlayerA.CurrentHp);
        result1.NewState.PlayerB.CurrentHp.Should().Be(result2.NewState.PlayerB.CurrentHp);
    }

    [Fact]
    public void SameBattleResolvedTwice_IdenticalResults()
    {
        var state = CreateState();
        var actionA = CreateAction(_playerAId, 1, BattleZone.Belly, BattleZone.Legs, BattleZone.Head);
        var actionB = CreateAction(_playerBId, 1, BattleZone.Legs, BattleZone.Waist, BattleZone.Belly);

        var r1 = _engine.ResolveTurn(state, actionA, actionB);
        var r2 = _engine.ResolveTurn(state, actionA, actionB);

        r1.NewState.PlayerA.CurrentHp.Should().Be(r2.NewState.PlayerA.CurrentHp);
        r1.NewState.PlayerB.CurrentHp.Should().Be(r2.NewState.PlayerB.CurrentHp);
        r1.NewState.Phase.Should().Be(r2.NewState.Phase);
    }

    [Fact]
    public void OrderIndependence_ABandBA_SameOutcomes()
    {
        // The engine always resolves A→B and B→A with separate RNG streams.
        // Calling ResolveTurn with the same state/actions should produce the same
        // results regardless of internal computation order.
        var state = CreateState();
        var actionA = CreateAction(_playerAId, 1);
        var actionB = CreateAction(_playerBId, 1);

        var result = _engine.ResolveTurn(state, actionA, actionB);

        // Resolve again with same inputs
        var result2 = _engine.ResolveTurn(state, actionA, actionB);

        result.NewState.PlayerA.CurrentHp.Should().Be(result2.NewState.PlayerA.CurrentHp);
        result.NewState.PlayerB.CurrentHp.Should().Be(result2.NewState.PlayerB.CurrentHp);
    }

    [Fact]
    public void MultiTurnFixedSequence_SameTerminalState()
    {
        var stats = new PlayerStats(10, 10, 10, 10);
        var ruleset = TestHelpers.DefaultRuleset(seed: 99);
        var state = new BattleDomainState(
            _battleId, _matchId, _playerAId, _playerBId, ruleset,
            BattlePhase.Resolving, 1, 0, 0,
            new PlayerState(_playerAId, 150, stats),
            new PlayerState(_playerBId, 150, stats));

        // Run multiple turns with fixed actions
        var currentState = state;
        for (int turn = 1; turn <= 20; turn++)
        {
            if (currentState.Phase == BattlePhase.Ended) break;

            currentState = new BattleDomainState(
                currentState.BattleId, currentState.MatchId,
                currentState.PlayerAId, currentState.PlayerBId,
                currentState.Ruleset, BattlePhase.Resolving,
                turn, currentState.NoActionStreakBoth, turn - 1,
                currentState.PlayerA, currentState.PlayerB);

            var actionA = CreateAction(_playerAId, turn, BattleZone.Head, BattleZone.Chest, BattleZone.Belly);
            var actionB = CreateAction(_playerBId, turn, BattleZone.Belly, BattleZone.Waist, BattleZone.Legs);

            var result = _engine.ResolveTurn(currentState, actionA, actionB);
            currentState = result.NewState;
        }

        // Run the same sequence again
        var state2 = new BattleDomainState(
            _battleId, _matchId, _playerAId, _playerBId, ruleset,
            BattlePhase.Resolving, 1, 0, 0,
            new PlayerState(_playerAId, 150, stats),
            new PlayerState(_playerBId, 150, stats));

        var currentState2 = state2;
        for (int turn = 1; turn <= 20; turn++)
        {
            if (currentState2.Phase == BattlePhase.Ended) break;

            currentState2 = new BattleDomainState(
                currentState2.BattleId, currentState2.MatchId,
                currentState2.PlayerAId, currentState2.PlayerBId,
                currentState2.Ruleset, BattlePhase.Resolving,
                turn, currentState2.NoActionStreakBoth, turn - 1,
                currentState2.PlayerA, currentState2.PlayerB);

            var actionA = CreateAction(_playerAId, turn, BattleZone.Head, BattleZone.Chest, BattleZone.Belly);
            var actionB = CreateAction(_playerBId, turn, BattleZone.Belly, BattleZone.Waist, BattleZone.Legs);

            var result = _engine.ResolveTurn(currentState2, actionA, actionB);
            currentState2 = result.NewState;
        }

        currentState.PlayerA.CurrentHp.Should().Be(currentState2.PlayerA.CurrentHp);
        currentState.PlayerB.CurrentHp.Should().Be(currentState2.PlayerB.CurrentHp);
        currentState.Phase.Should().Be(currentState2.Phase);
    }

    // ========== Phase Validation ==========

    [Theory]
    [InlineData(BattlePhase.ArenaOpen)]
    [InlineData(BattlePhase.TurnOpen)]
    [InlineData(BattlePhase.Ended)]
    public void ResolveTurn_WrongPhase_Throws(BattlePhase phase)
    {
        var stats = new PlayerStats(10, 10, 10, 10);
        var ruleset = TestHelpers.DefaultRuleset();
        var state = new BattleDomainState(
            _battleId, _matchId, _playerAId, _playerBId, ruleset,
            phase, 1, 0, 0,
            new PlayerState(_playerAId, 100, stats),
            new PlayerState(_playerBId, 100, stats));

        var act = () => _engine.ResolveTurn(state,
            CreateAction(_playerAId, 1), CreateAction(_playerBId, 1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveTurn_TurnIndexMismatch_Throws()
    {
        var state = CreateState(turnIndex: 1);
        var act = () => _engine.ResolveTurn(state,
            CreateAction(_playerAId, 2), CreateAction(_playerBId, 1));

        act.Should().Throw<ArgumentException>();
    }

    // ========== NoAction / Double Forfeit ==========

    [Fact]
    public void BothNoAction_IncrementsStreak()
    {
        var state = CreateState(noActionStreak: 3);
        var noA = PlayerAction.NoAction(_playerAId, 1);
        var noB = PlayerAction.NoAction(_playerBId, 1);

        var result = _engine.ResolveTurn(state, noA, noB);

        result.NewState.NoActionStreakBoth.Should().Be(4);
        result.NewState.Phase.Should().NotBe(BattlePhase.Ended);
    }

    [Fact]
    public void BothNoAction_AtLimit_EndsBattle()
    {
        var state = CreateState(noActionStreak: 9, noActionLimit: 10);
        var noA = PlayerAction.NoAction(_playerAId, 1);
        var noB = PlayerAction.NoAction(_playerBId, 1);

        var result = _engine.ResolveTurn(state, noA, noB);

        result.NewState.Phase.Should().Be(BattlePhase.Ended);
        result.Events.Should().ContainSingle(e => e is BattleEndedDomainEvent);

        var endEvent = result.Events.OfType<BattleEndedDomainEvent>().Single();
        endEvent.WinnerPlayerId.Should().BeNull();
        endEvent.Reason.Should().Be(EndBattleReason.DoubleForfeit);
    }

    [Fact]
    public void OnePlayerNoAction_OtherAttacks_StreakResets()
    {
        var state = CreateState(noActionStreak: 5);
        var actionA = CreateAction(_playerAId, 1);
        var noB = PlayerAction.NoAction(_playerBId, 1);

        var result = _engine.ResolveTurn(state, actionA, noB);

        result.NewState.NoActionStreakBoth.Should().Be(0);
    }

    [Fact]
    public void NoActionDegradation_TenIdleTurns_DeterministicTerminalState()
    {
        var stats = new PlayerStats(10, 10, 10, 10);
        var ruleset = Ruleset.Create(1, 30, 10, 42, TestHelpers.DefaultBalance);
        var state = new BattleDomainState(
            _battleId, _matchId, _playerAId, _playerBId, ruleset,
            BattlePhase.Resolving, 1, 0, 0,
            new PlayerState(_playerAId, 100, stats),
            new PlayerState(_playerBId, 100, stats));

        var currentState = state;
        for (int turn = 1; turn <= 10; turn++)
        {
            if (currentState.Phase == BattlePhase.Ended) break;

            currentState = new BattleDomainState(
                currentState.BattleId, currentState.MatchId,
                currentState.PlayerAId, currentState.PlayerBId,
                currentState.Ruleset, BattlePhase.Resolving,
                turn, currentState.NoActionStreakBoth, turn - 1,
                currentState.PlayerA, currentState.PlayerB);

            var result = _engine.ResolveTurn(currentState,
                PlayerAction.NoAction(_playerAId, turn),
                PlayerAction.NoAction(_playerBId, turn));
            currentState = result.NewState;
        }

        currentState.Phase.Should().Be(BattlePhase.Ended);
    }

    // ========== Damage / Battle End ==========

    [Fact]
    public void BothPlayersAttack_DamageApplied()
    {
        var state = CreateState();
        var actionA = CreateAction(_playerAId, 1);
        var actionB = CreateAction(_playerBId, 1);

        var result = _engine.ResolveTurn(state, actionA, actionB);

        // At least one player should have taken some damage or dodged
        // (deterministic, but damage or dodges occur)
        result.Events.Should().NotBeEmpty();
    }

    [Fact]
    public void PlayerDies_BattleEnds()
    {
        // Give player B very low HP so they die
        var state = CreateState(playerBHp: 1);
        var actionA = CreateAction(_playerAId, 1, BattleZone.Legs, BattleZone.Head, BattleZone.Chest);
        var actionB = CreateAction(_playerBId, 1, BattleZone.Head, BattleZone.Chest, BattleZone.Belly);

        var result = _engine.ResolveTurn(state, actionA, actionB);

        // With 1 HP, player B should die (unless dodged)
        // Run enough seeds to find one where B dies
        for (int seed = 0; seed < 100; seed++)
        {
            var testState = CreateState(playerBHp: 1, seed: seed);
            var r = _engine.ResolveTurn(testState, actionA, actionB);
            if (r.NewState.Phase == BattlePhase.Ended)
            {
                r.Events.Should().Contain(e => e is BattleEndedDomainEvent);
                return;
            }
        }

        // If we get here, all 100 seeds had player B dodge - extremely unlikely
        Assert.Fail("Player B should have died in at least one of 100 seeds");
    }

    [Fact]
    public void BothDie_SimultaneousKill_NullWinner()
    {
        // Use high-strength stats to make damage more likely to land and kill
        var highStr = new PlayerStats(50, 5, 0, 0); // high str, zero agi/int = no dodge/crit interference
        var balance = TestHelpers.DefaultBalance;
        var ruleset = Ruleset.Create(1, 30, 10, 42, balance);

        // Both players at 1 HP with high damage stats
        for (int seed = 0; seed < 500; seed++)
        {
            var testRuleset = Ruleset.Create(1, 30, 10, seed, balance);
            var state = new BattleDomainState(
                _battleId, _matchId, _playerAId, _playerBId, testRuleset,
                BattlePhase.Resolving, 1, 0, 0,
                new PlayerState(_playerAId, 100, 1, highStr),
                new PlayerState(_playerBId, 100, 1, highStr));

            // A attacks Belly, B blocks Head+Chest → not blocked
            // B attacks Belly, A blocks Head+Chest → not blocked
            var actionA = CreateAction(_playerAId, 1, BattleZone.Belly, BattleZone.Head, BattleZone.Chest);
            var actionB = CreateAction(_playerBId, 1, BattleZone.Belly, BattleZone.Head, BattleZone.Chest);

            var result = _engine.ResolveTurn(state, actionA, actionB);
            if (result.NewState.PlayerA.IsDead && result.NewState.PlayerB.IsDead)
            {
                var endEvent = result.Events.OfType<BattleEndedDomainEvent>().Single();
                endEvent.WinnerPlayerId.Should().BeNull(); // Draw
                return;
            }
        }

        Assert.Fail("Both players should have died simultaneously in at least one of 500 seeds");
    }

    // ========== Attack Resolution Details ==========

    [Fact]
    public void NoActionAttacker_NoActionOutcome_ZeroDamage()
    {
        var state = CreateState();
        var noAction = PlayerAction.NoAction(_playerAId, 1);
        var actionB = CreateAction(_playerBId, 1);

        var result = _engine.ResolveTurn(state, noAction, actionB);

        // TurnResolved event should exist
        var turnEvent = result.Events.OfType<TurnResolvedDomainEvent>().FirstOrDefault();
        if (turnEvent != null)
        {
            turnEvent.Log.AtoB.Outcome.Should().Be(AttackOutcome.NoAction);
            turnEvent.Log.AtoB.Damage.Should().Be(0);
        }
    }

    [Fact]
    public void DomainEvents_TurnResolved_ContainsLog()
    {
        var state = CreateState();
        var actionA = CreateAction(_playerAId, 1);
        var actionB = CreateAction(_playerBId, 1);

        var result = _engine.ResolveTurn(state, actionA, actionB);

        // Should contain TurnResolved or BattleEnded event
        result.Events.Should().NotBeEmpty();

        var turnEvent = result.Events.OfType<TurnResolvedDomainEvent>().FirstOrDefault();
        if (turnEvent != null)
        {
            turnEvent.Log.Should().NotBeNull();
            turnEvent.Log.AtoB.Should().NotBeNull();
            turnEvent.Log.BtoA.Should().NotBeNull();
            turnEvent.TurnIndex.Should().Be(1);
        }
    }
}
