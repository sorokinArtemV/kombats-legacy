using FluentAssertions;
using Kombats.Battle.Realtime.Contracts;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;
using Xunit;

namespace Kombats.Bff.Application.Tests.Narration;

public class NarrationPipelineTests
{
    private readonly NarrationPipeline _pipeline;
    private readonly Guid _battleId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly Guid _playerAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _playerBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public NarrationPipelineTests()
    {
        _pipeline = new NarrationPipeline(
            new InMemoryTemplateCatalog(),
            new DeterministicTemplateSelector(),
            new PlaceholderNarrationRenderer(),
            new DefaultCommentatorPolicy(),
            new DefaultFeedAssembler());
    }

    private BattleParticipantSnapshot CreateParticipants() =>
        new(_playerAId, _playerBId, "Alice", "Bob");

    private TurnResolvedRealtime CreateTurnResolved(
        AttackOutcomeRealtime atoBOutcome = AttackOutcomeRealtime.Hit,
        int atoBDamage = 10,
        AttackOutcomeRealtime btoAOutcome = AttackOutcomeRealtime.Hit,
        int btoADamage = 10,
        int turnIndex = 1)
    {
        return new TurnResolvedRealtime
        {
            BattleId = _battleId,
            TurnIndex = turnIndex,
            PlayerAAction = "Attack",
            PlayerBAction = "Attack",
            Log = new TurnResolutionLogRealtime
            {
                BattleId = _battleId,
                TurnIndex = turnIndex,
                AtoB = new AttackResolutionRealtime
                {
                    AttackerId = _playerAId, DefenderId = _playerBId, TurnIndex = turnIndex,
                    Outcome = atoBOutcome, Damage = atoBDamage, AttackZone = "Head"
                },
                BtoA = new AttackResolutionRealtime
                {
                    AttackerId = _playerBId, DefenderId = _playerAId, TurnIndex = turnIndex,
                    Outcome = btoAOutcome, Damage = btoADamage, AttackZone = "Chest"
                }
            }
        };
    }

    // ========== Turn Feed Tests ==========

    [Fact]
    public void GenerateTurnFeed_Hit_ProducesTwoEntries()
    {
        var turnResolved = CreateTurnResolved();
        var state = new CommentatorState { FirstBloodFired = true };

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            90, 90, 100, 100);

        result.Entries.Should().HaveCount(2);
        result.Entries[0].Sequence.Should().Be(0);
        result.Entries[1].Sequence.Should().Be(1);
        result.Entries[0].Kind.Should().Be(FeedEntryKind.AttackHit);
        result.Entries[1].Kind.Should().Be(FeedEntryKind.AttackHit);
    }

    [Fact]
    public void GenerateTurnFeed_Crit_ProducesCritEntry()
    {
        var turnResolved = CreateTurnResolved(
            atoBOutcome: AttackOutcomeRealtime.CriticalHit, atoBDamage: 30);
        var state = new CommentatorState { FirstBloodFired = true };

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            70, 100, 100, 100);

        result.Entries[0].Kind.Should().Be(FeedEntryKind.AttackCrit);
    }

    [Fact]
    public void GenerateTurnFeed_Dodge_ProducesDodgeEntry()
    {
        var turnResolved = CreateTurnResolved(
            atoBOutcome: AttackOutcomeRealtime.Dodged, atoBDamage: 0);
        var state = new CommentatorState { FirstBloodFired = true };

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            100, 100, 100, 100);

        result.Entries[0].Kind.Should().Be(FeedEntryKind.AttackDodge);
    }

    [Fact]
    public void GenerateTurnFeed_Block_ProducesBlockEntry()
    {
        var turnResolved = CreateTurnResolved(
            atoBOutcome: AttackOutcomeRealtime.Blocked, atoBDamage: 0);
        var state = new CommentatorState { FirstBloodFired = true };

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            100, 100, 100, 100);

        result.Entries[0].Kind.Should().Be(FeedEntryKind.AttackBlock);
    }

    [Fact]
    public void GenerateTurnFeed_NoAction_ProducesNoActionEntry()
    {
        var turnResolved = CreateTurnResolved(
            atoBOutcome: AttackOutcomeRealtime.NoAction, atoBDamage: 0);
        var state = new CommentatorState { FirstBloodFired = true };

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            100, 100, 100, 100);

        result.Entries[0].Kind.Should().Be(FeedEntryKind.AttackNoAction);
    }

    [Fact]
    public void GenerateTurnFeed_WithCommentary_ProducesThreeEntries()
    {
        var turnResolved = CreateTurnResolved(atoBDamage: 5, btoADamage: 0);
        var state = new CommentatorState(); // First blood not fired yet

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            100, 95, 100, 100);

        result.Entries.Should().HaveCount(3);
        result.Entries[2].Kind.Should().Be(FeedEntryKind.CommentaryFirstBlood);
        result.Entries[2].Sequence.Should().Be(2);
    }

    [Fact]
    public void GenerateTurnFeed_NullLog_ProducesNoEntries()
    {
        var turnResolved = new TurnResolvedRealtime
        {
            BattleId = _battleId, TurnIndex = 1,
            PlayerAAction = "NoAction", PlayerBAction = "NoAction",
            Log = null
        };
        var state = new CommentatorState();

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            100, 100, 100, 100);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public void GenerateTurnFeed_TextContainsNames()
    {
        var turnResolved = CreateTurnResolved();
        var state = new CommentatorState { FirstBloodFired = true };

        var result = _pipeline.GenerateTurnFeed(
            _battleId, turnResolved, CreateParticipants(), state,
            90, 90, 100, 100);

        // A→B: Alice attacks Bob
        result.Entries[0].Text.Should().Contain("Alice");
        // B→A: Bob attacks Alice
        result.Entries[1].Text.Should().Contain("Bob");
    }

    // ========== Battle Start Tests ==========

    [Fact]
    public void GenerateBattleStartFeed_ProducesOneEntry()
    {
        var result = _pipeline.GenerateBattleStartFeed(_battleId, CreateParticipants());

        result.Entries.Should().HaveCount(1);
        result.Entries[0].Kind.Should().Be(FeedEntryKind.BattleStart);
        result.Entries[0].TurnIndex.Should().Be(0);
        result.Entries[0].Sequence.Should().Be(0);
        result.Entries[0].Text.Should().Contain("Alice");
        result.Entries[0].Text.Should().Contain("Bob");
    }

    // ========== Battle End Tests ==========

    [Fact]
    public void GenerateBattleEndFeed_Victory_ProducesDefeatedAndVictoryEntries()
    {
        var ended = new BattleEndedRealtime
        {
            BattleId = _battleId,
            Reason = BattleEndReasonRealtime.Normal,
            WinnerPlayerId = _playerAId,
            EndedAt = DateTimeOffset.UtcNow
        };
        var state = new CommentatorState();

        var result = _pipeline.GenerateBattleEndFeed(_battleId, ended, CreateParticipants(), state);

        // Defeat + Victory + Commentary(knockout) = 3
        result.Entries.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Entries[0].Kind.Should().Be(FeedEntryKind.DefeatKnockout);
        result.Entries[1].Kind.Should().Be(FeedEntryKind.BattleEndVictory);
    }

    [Fact]
    public void GenerateBattleEndFeed_DoubleForfeit_ProducesForfeitAndDrawEntries()
    {
        var ended = new BattleEndedRealtime
        {
            BattleId = _battleId,
            Reason = BattleEndReasonRealtime.DoubleForfeit,
            WinnerPlayerId = null,
            EndedAt = DateTimeOffset.UtcNow
        };
        var state = new CommentatorState();

        var result = _pipeline.GenerateBattleEndFeed(_battleId, ended, CreateParticipants(), state);

        result.Entries.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Entries[0].Kind.Should().Be(FeedEntryKind.BattleEndForfeit);
        result.Entries[1].Kind.Should().Be(FeedEntryKind.BattleEndDraw);
    }

    // ========== GenerateFullBattleFeed Tests ==========

    [Fact]
    public void GenerateFullBattleFeed_ProducesSameOutputAsSequentialCalls()
    {
        var history = new BattleHistory
        {
            BattleId = _battleId,
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 100,
            PlayerBMaxHp = 100,
            EndReason = "Normal",
            WinnerPlayerId = _playerAId,
            Turns =
            [
                new BattleHistoryTurn
                {
                    TurnIndex = 1,
                    AtoBOutcome = "Hit", AtoBDamage = 15, AtoBAttackZone = "Head",
                    BtoAOutcome = "Dodged", BtoADamage = 0, BtoAAttackZone = "Chest",
                    PlayerAHpAfter = 100, PlayerBHpAfter = 85
                },
                new BattleHistoryTurn
                {
                    TurnIndex = 2,
                    AtoBOutcome = "CriticalHit", AtoBDamage = 85, AtoBAttackZone = "Head",
                    BtoAOutcome = "Hit", BtoADamage = 10, BtoAAttackZone = "Chest",
                    PlayerAHpAfter = 90, PlayerBHpAfter = 0
                }
            ]
        };

        // Generate via full-batch
        var fullResult = _pipeline.GenerateFullBattleFeed(history);

        // Generate via sequential calls (simulating live)
        var participants = new BattleParticipantSnapshot(_playerAId, _playerBId, "Alice", "Bob");
        var commentatorState = new CommentatorState();
        var sequentialEntries = new List<BattleFeedEntry>();

        var startFeed = _pipeline.GenerateBattleStartFeed(_battleId, participants);
        sequentialEntries.AddRange(startFeed.Entries);

        // Turn 1
        var turn1 = new TurnResolvedRealtime
        {
            BattleId = _battleId, TurnIndex = 1,
            PlayerAAction = "Attack", PlayerBAction = "Attack",
            Log = new TurnResolutionLogRealtime
            {
                BattleId = _battleId, TurnIndex = 1,
                AtoB = new AttackResolutionRealtime
                {
                    AttackerId = _playerAId, DefenderId = _playerBId, TurnIndex = 1,
                    Outcome = AttackOutcomeRealtime.Hit, Damage = 15, AttackZone = "Head"
                },
                BtoA = new AttackResolutionRealtime
                {
                    AttackerId = _playerBId, DefenderId = _playerAId, TurnIndex = 1,
                    Outcome = AttackOutcomeRealtime.Dodged, Damage = 0, AttackZone = "Chest"
                }
            }
        };
        // Live: TurnResolved arrives before BattleStateUpdated, so HP is pre-turn (maxHP at start)
        var turn1Feed = _pipeline.GenerateTurnFeed(
            _battleId, turn1, participants, commentatorState, 100, 100, 100, 100);
        sequentialEntries.AddRange(turn1Feed.Entries);

        // Turn 2 — HP now reflects post-turn-1 values (from BattleStateUpdated after turn 1)
        var turn2 = new TurnResolvedRealtime
        {
            BattleId = _battleId, TurnIndex = 2,
            PlayerAAction = "Attack", PlayerBAction = "Attack",
            Log = new TurnResolutionLogRealtime
            {
                BattleId = _battleId, TurnIndex = 2,
                AtoB = new AttackResolutionRealtime
                {
                    AttackerId = _playerAId, DefenderId = _playerBId, TurnIndex = 2,
                    Outcome = AttackOutcomeRealtime.CriticalHit, Damage = 85, AttackZone = "Head"
                },
                BtoA = new AttackResolutionRealtime
                {
                    AttackerId = _playerBId, DefenderId = _playerAId, TurnIndex = 2,
                    Outcome = AttackOutcomeRealtime.Hit, Damage = 10, AttackZone = "Chest"
                }
            }
        };
        // Live: HP is post-turn-1 (100, 85), not post-turn-2
        var turn2Feed = _pipeline.GenerateTurnFeed(
            _battleId, turn2, participants, commentatorState, 100, 85, 100, 100);
        sequentialEntries.AddRange(turn2Feed.Entries);

        // Battle end
        var ended = new BattleEndedRealtime
        {
            BattleId = _battleId,
            Reason = BattleEndReasonRealtime.Normal,
            WinnerPlayerId = _playerAId,
            EndedAt = DateTimeOffset.MinValue
        };
        var endFeed = _pipeline.GenerateBattleEndFeed(_battleId, ended, participants, commentatorState);
        sequentialEntries.AddRange(endFeed.Entries);

        // Compare: same count, same keys, same text
        fullResult.Should().HaveCount(sequentialEntries.Count);
        for (int i = 0; i < fullResult.Length; i++)
        {
            fullResult[i].Key.Should().Be(sequentialEntries[i].Key);
            fullResult[i].Text.Should().Be(sequentialEntries[i].Text);
            fullResult[i].Kind.Should().Be(sequentialEntries[i].Kind);
        }
    }

    [Fact]
    public void GenerateFullBattleFeed_EmptyTurns_ProducesStartOnly()
    {
        var history = new BattleHistory
        {
            BattleId = _battleId,
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 100,
            PlayerBMaxHp = 100,
            EndReason = null,
            WinnerPlayerId = null,
            Turns = []
        };

        var result = _pipeline.GenerateFullBattleFeed(history);

        // Just the battle start entry
        result.Should().HaveCount(1);
        result[0].Kind.Should().Be(FeedEntryKind.BattleStart);
    }

    [Fact]
    public void GenerateFullBattleFeed_IsDeterministic()
    {
        var history = new BattleHistory
        {
            BattleId = _battleId,
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 100,
            PlayerBMaxHp = 100,
            EndReason = "Normal",
            WinnerPlayerId = _playerAId,
            Turns =
            [
                new BattleHistoryTurn
                {
                    TurnIndex = 1,
                    AtoBOutcome = "Hit", AtoBDamage = 100,
                    BtoAOutcome = "NoAction", BtoADamage = 0,
                    PlayerAHpAfter = 100, PlayerBHpAfter = 0
                }
            ]
        };

        var result1 = _pipeline.GenerateFullBattleFeed(history);
        var result2 = _pipeline.GenerateFullBattleFeed(history);

        result1.Should().HaveCount(result2.Length);
        for (int i = 0; i < result1.Length; i++)
        {
            result1[i].Key.Should().Be(result2[i].Key);
            result1[i].Text.Should().Be(result2[i].Text);
        }
    }

    [Fact]
    public void GenerateFullBattleFeed_EntryKeysAreUnique()
    {
        var history = new BattleHistory
        {
            BattleId = _battleId,
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 100,
            PlayerBMaxHp = 100,
            EndReason = "Normal",
            WinnerPlayerId = _playerAId,
            Turns =
            [
                new BattleHistoryTurn
                {
                    TurnIndex = 1,
                    AtoBOutcome = "Hit", AtoBDamage = 15,
                    BtoAOutcome = "Hit", BtoADamage = 10,
                    PlayerAHpAfter = 90, PlayerBHpAfter = 85
                },
                new BattleHistoryTurn
                {
                    TurnIndex = 2,
                    AtoBOutcome = "Hit", AtoBDamage = 85,
                    BtoAOutcome = "Hit", BtoADamage = 10,
                    PlayerAHpAfter = 80, PlayerBHpAfter = 0
                }
            ]
        };

        var result = _pipeline.GenerateFullBattleFeed(history);
        var keys = result.Select(e => e.Key).ToList();
        keys.Should().OnlyHaveUniqueItems();
    }
}
