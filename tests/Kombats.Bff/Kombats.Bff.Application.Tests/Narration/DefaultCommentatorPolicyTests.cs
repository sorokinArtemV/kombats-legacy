using FluentAssertions;
using Kombats.Battle.Realtime.Contracts;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Feed;
using Xunit;

namespace Kombats.Bff.Application.Tests.Narration;

public class DefaultCommentatorPolicyTests
{
    private readonly DefaultCommentatorPolicy _policy = new();
    private readonly Guid _playerAId = Guid.NewGuid();
    private readonly Guid _playerBId = Guid.NewGuid();

    private BattleParticipantSnapshot CreateParticipants() =>
        new(_playerAId, _playerBId, "Alice", "Bob");

    private TurnResolutionLogRealtime CreateLog(
        AttackOutcomeRealtime atoBOutcome = AttackOutcomeRealtime.Hit,
        int atoBDamage = 10,
        AttackOutcomeRealtime btoAOutcome = AttackOutcomeRealtime.Hit,
        int btoADamage = 10)
    {
        return new TurnResolutionLogRealtime
        {
            BattleId = Guid.NewGuid(),
            TurnIndex = 1,
            AtoB = new AttackResolutionRealtime
            {
                AttackerId = _playerAId, DefenderId = _playerBId, TurnIndex = 1,
                Outcome = atoBOutcome, Damage = atoBDamage
            },
            BtoA = new AttackResolutionRealtime
            {
                AttackerId = _playerBId, DefenderId = _playerAId, TurnIndex = 1,
                Outcome = btoAOutcome, Damage = btoADamage
            }
        };
    }

    [Fact]
    public void FirstBlood_Fires_WhenFirstDamage()
    {
        var state = new CommentatorState();
        var log = CreateLog(atoBDamage: 5, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 95, 100, 100, 100);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryFirstBlood);
        state.FirstBloodFired.Should().BeTrue();
    }

    [Fact]
    public void FirstBlood_DoesNotFireTwice()
    {
        var state = new CommentatorState { FirstBloodFired = true };
        var log = CreateLog(atoBDamage: 5, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 95, 100, 100, 100);

        // Should not be first_blood (already fired), could be something else or null
        if (cue is not null)
            cue.Kind.Should().NotBe(FeedEntryKind.CommentaryFirstBlood);
    }

    [Fact]
    public void DoubleDodge_Fires()
    {
        var state = new CommentatorState { FirstBloodFired = true };
        var log = CreateLog(
            atoBOutcome: AttackOutcomeRealtime.Dodged, atoBDamage: 0,
            btoAOutcome: AttackOutcomeRealtime.Dodged, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 100, 100, 100);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryMutualMiss);
        state.DoubleDodgeFired.Should().BeTrue();
    }

    [Fact]
    public void DoubleNoAction_FiresStalemate()
    {
        var state = new CommentatorState { FirstBloodFired = true };
        var log = CreateLog(
            atoBOutcome: AttackOutcomeRealtime.NoAction, atoBDamage: 0,
            btoAOutcome: AttackOutcomeRealtime.NoAction, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 100, 100, 100);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryStalemate);
        state.DoubleNoActionFired.Should().BeTrue();
    }

    [Fact]
    public void NearDeath_Fires_WhenBelow25Percent()
    {
        var state = new CommentatorState { FirstBloodFired = true };
        var log = CreateLog(atoBDamage: 5, btoADamage: 5);

        // Player A at 20 / 100 = 20% < 25%
        var cue = _policy.Evaluate(log, CreateParticipants(), state, 20, 100, 100, 100);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryNearDeath);
        state.NearDeathPlayerAFired.Should().BeTrue();
    }

    [Fact]
    public void NearDeath_FirsPerPlayer_Independently()
    {
        var state = new CommentatorState { FirstBloodFired = true };
        var log = CreateLog(atoBDamage: 5, btoADamage: 5);

        // Player A near death
        _policy.Evaluate(log, CreateParticipants(), state, 20, 100, 100, 100);
        state.NearDeathPlayerAFired.Should().BeTrue();
        state.NearDeathPlayerBFired.Should().BeFalse();

        // Player B near death (next turn)
        var cue = _policy.Evaluate(log, CreateParticipants(), state, 20, 15, 100, 100);
        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryNearDeath);
        state.NearDeathPlayerBFired.Should().BeTrue();
    }

    [Fact]
    public void BigHit_Fires_WhenDamageExceeds50PercentMaxHp()
    {
        var state = new CommentatorState { FirstBloodFired = true };
        // A→B deals 60 damage, B's maxHP is 100, 60/100 > 50%
        var log = CreateLog(atoBDamage: 60, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 40, 100, 100);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryBigHit);
    }

    [Fact]
    public void BigHit_MaxTwoFires()
    {
        var state = new CommentatorState { FirstBloodFired = true, BigHitCount = 2 };
        var log = CreateLog(atoBDamage: 60, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 40, 100, 100);

        // Big hit already maxed, should not fire big_hit
        if (cue is not null)
            cue.Kind.Should().NotBe(FeedEntryKind.CommentaryBigHit);
    }

    [Fact]
    public void NearDeath_WinsOver_BigHit()
    {
        var state = new CommentatorState { FirstBloodFired = true };
        // Big hit: A→B 60 damage (60/100 > 50%) — non-lethal for B (100 → 40)
        // Near death: Player A at 20/100 = 20%, takes no damage this turn (alive after)
        var log = CreateLog(atoBDamage: 60, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 20, 100, 100, 100);

        // Near death has higher priority
        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryNearDeath);
    }

    [Fact]
    public void NearDeath_DoesNotFire_OnLethalDamageThisTurn()
    {
        // Killing-blow turn: defeat.knockout covers the death. Firing
        // near_death with pre-turn HP would misleadingly say "Only X HP left"
        // about a player who just hit zero.
        var state = new CommentatorState { FirstBloodFired = true };
        // A→B 60 damage. Player B at 20 entering takes lethal damage (20 - 60 ≤ 0).
        var log = CreateLog(atoBDamage: 60, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 20, 100, 100);

        if (cue is not null)
            cue.Kind.Should().NotBe(FeedEntryKind.CommentaryNearDeath);
        state.NearDeathPlayerBFired.Should().BeFalse();
    }

    [Fact]
    public void NearDeath_Fires_WhenLowHp_AndDamageIsNonLethal()
    {
        // Sanity: low pre-turn HP + survivable damage still triggers near_death.
        var state = new CommentatorState { FirstBloodFired = true };
        // A→B 5 damage, B at 20 entering, post-turn 15 (alive).
        var log = CreateLog(atoBDamage: 5, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 20, 100, 100);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryNearDeath);
        state.NearDeathPlayerBFired.Should().BeTrue();
    }

    [Fact]
    public void Max1PerTurn_EnforcedByReturningFirstMatch()
    {
        var state = new CommentatorState();
        // First blood + big hit eligible — only first blood fires (lower priority but first_blood is priority 3, big_hit is 2... wait)
        // Actually priority order: near-death > big-hit > first-blood
        // So big-hit would fire, not first-blood, if both eligible
        var log = CreateLog(atoBDamage: 60, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 40, 100, 100);

        // Only one cue returned (anti-spam: max 1 per turn)
        cue.Should().NotBeNull();
        // Big hit fires (higher priority than first blood)
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryBigHit);
    }

    [Fact]
    public void Knockout_Fires_OnNormalEnd()
    {
        var state = new CommentatorState();
        var winnerId = _playerAId;

        var cue = _policy.EvaluateBattleEnd(
            BattleEndReasonRealtime.Normal, winnerId, CreateParticipants(), state);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryKnockout);
        state.KnockoutFired.Should().BeTrue();
    }

    [Fact]
    public void Draw_Fires_OnDoubleForfeit()
    {
        var state = new CommentatorState();

        var cue = _policy.EvaluateBattleEnd(
            BattleEndReasonRealtime.DoubleForfeit, null, CreateParticipants(), state);

        cue.Should().NotBeNull();
        cue!.Kind.Should().Be(FeedEntryKind.CommentaryDraw);
        state.DrawFired.Should().BeTrue();
    }

    [Fact]
    public void NoDamage_NoTriggers()
    {
        var state = new CommentatorState();
        var log = CreateLog(
            atoBOutcome: AttackOutcomeRealtime.Blocked, atoBDamage: 0,
            btoAOutcome: AttackOutcomeRealtime.Blocked, btoADamage: 0);

        var cue = _policy.Evaluate(log, CreateParticipants(), state, 100, 100, 100, 100);

        cue.Should().BeNull();
    }
}
