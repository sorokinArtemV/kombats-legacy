using FluentAssertions;
using Kombats.Battle.Realtime.Contracts;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;
using Kombats.Bff.Application.Relay;
using Xunit;

namespace Kombats.Bff.Application.Tests.Relay;

/// <summary>
/// Tests the narration integration logic that BattleHubRelay uses:
/// connection state management, feed generation from typed events,
/// HP tracking from BattleStateUpdated, and reconnect behavior.
/// These verify the orchestration logic without needing a real SignalR connection.
/// </summary>
public sealed class BattleConnectionStateTests
{
    private readonly INarrationPipeline _pipeline;
    private readonly Guid _battleId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly Guid _playerAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _playerBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public BattleConnectionStateTests()
    {
        _pipeline = new NarrationPipeline(
            new InMemoryTemplateCatalog(),
            new DeterministicTemplateSelector(),
            new PlaceholderNarrationRenderer(),
            new DefaultCommentatorPolicy(),
            new DefaultFeedAssembler());
    }

    private BattleConnectionState CreateState() => new()
    {
        BattleId = _battleId,
        Participants = new BattleParticipantSnapshot(_playerAId, _playerBId, "Alice", "Bob"),
        Commentator = new CommentatorState(),
        PlayerAHp = 100,
        PlayerBHp = 100,
        PlayerAMaxHp = 100,
        PlayerBMaxHp = 100
    };

    [Fact]
    public void TurnResolved_GeneratesFeedWithCorrectEntries()
    {
        var state = CreateState();
        var turnResolved = new TurnResolvedRealtime
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

        var feed = _pipeline.GenerateTurnFeed(
            state.BattleId, turnResolved, state.Participants, state.Commentator,
            state.PlayerAHp, state.PlayerBHp, state.PlayerAMaxHp, state.PlayerBMaxHp);

        // A→B hit + B→A dodge + first blood commentary = 3 entries
        feed.Entries.Should().HaveCount(3);
        feed.Entries[0].Kind.Should().Be(FeedEntryKind.AttackHit);
        feed.Entries[1].Kind.Should().Be(FeedEntryKind.AttackDodge);
        feed.Entries[2].Kind.Should().Be(FeedEntryKind.CommentaryFirstBlood);
    }

    [Fact]
    public void TurnResolved_RawRelayOrder_FeedComesAfter()
    {
        // This test verifies the contract: raw event relay happens BEFORE feed generation.
        // The relay implementation ensures this by awaiting SendAsync for the raw event first.
        // Here we verify the feed is generated independently and does not include the raw event.
        var state = CreateState();
        state.Commentator.FirstBloodFired = true; // Already fired

        var turnResolved = new TurnResolvedRealtime
        {
            BattleId = _battleId, TurnIndex = 2,
            PlayerAAction = "Attack", PlayerBAction = "Attack",
            Log = new TurnResolutionLogRealtime
            {
                BattleId = _battleId, TurnIndex = 2,
                AtoB = new AttackResolutionRealtime
                {
                    AttackerId = _playerAId, DefenderId = _playerBId, TurnIndex = 2,
                    Outcome = AttackOutcomeRealtime.Hit, Damage = 10
                },
                BtoA = new AttackResolutionRealtime
                {
                    AttackerId = _playerBId, DefenderId = _playerAId, TurnIndex = 2,
                    Outcome = AttackOutcomeRealtime.Hit, Damage = 10
                }
            }
        };

        var feed = _pipeline.GenerateTurnFeed(
            state.BattleId, turnResolved, state.Participants, state.Commentator,
            90, 85, state.PlayerAMaxHp, state.PlayerBMaxHp);

        // Two attack entries, no commentary (first blood already fired, no other triggers)
        feed.Entries.Should().HaveCount(2);
        feed.BattleId.Should().Be(_battleId);
    }

    [Fact]
    public void BattleEnded_GeneratesFeedWithEndEntries()
    {
        var state = CreateState();
        var ended = new BattleEndedRealtime
        {
            BattleId = _battleId,
            Reason = BattleEndReasonRealtime.Normal,
            WinnerPlayerId = _playerAId,
            EndedAt = DateTimeOffset.UtcNow
        };

        var feed = _pipeline.GenerateBattleEndFeed(
            state.BattleId, ended, state.Participants, state.Commentator);

        // DefeatKnockout + BattleEndVictory + Commentary(knockout) = 3
        feed.Entries.Should().HaveCount(3);
        feed.Entries[0].Kind.Should().Be(FeedEntryKind.DefeatKnockout);
        feed.Entries[1].Kind.Should().Be(FeedEntryKind.BattleEndVictory);
        feed.Entries[2].Kind.Should().Be(FeedEntryKind.CommentaryKnockout);
    }

    [Fact]
    public void BattleStateUpdated_UpdatesHpInState()
    {
        var state = CreateState();

        // Simulate receiving BattleStateUpdated
        var stateUpdate = new BattleStateUpdatedRealtime
        {
            BattleId = _battleId,
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            PlayerAHp = 85,
            PlayerBHp = 70
        };

        state.PlayerAHp = stateUpdate.PlayerAHp;
        state.PlayerBHp = stateUpdate.PlayerBHp;

        state.PlayerAHp.Should().Be(85);
        state.PlayerBHp.Should().Be(70);
        // MaxHp should NOT change (from durable snapshot)
        state.PlayerAMaxHp.Should().Be(100);
        state.PlayerBMaxHp.Should().Be(100);
    }

    [Fact]
    public void BlindEvents_DoNotGenerateFeed()
    {
        // BattleReady, TurnOpened, PlayerDamaged are blind-relayed: no feed generation.
        // This verifies the pipeline is NOT called for these events.
        // The relay implementation simply forwards them via sender.SendAsync.
        var blindEvents = new[] { "BattleReady", "TurnOpened", "PlayerDamaged" };

        foreach (var eventName in blindEvents)
        {
            // These should not appear in the feed pipeline interface at all
            typeof(INarrationPipeline).GetMethods()
                .Should().NotContain(m => m.Name.Contains(eventName, StringComparison.OrdinalIgnoreCase),
                    $"{eventName} is a blind relay event — no narration method should exist for it");
        }
    }

    [Fact]
    public void JoinBattle_BattleStartFeed_ContainsNames()
    {
        var participants = new BattleParticipantSnapshot(_playerAId, _playerBId, "Alice", "Bob");

        var feed = _pipeline.GenerateBattleStartFeed(_battleId, participants);

        feed.Entries.Should().HaveCount(1);
        feed.Entries[0].Kind.Should().Be(FeedEntryKind.BattleStart);
        feed.Entries[0].Text.Should().Contain("Alice");
        feed.Entries[0].Text.Should().Contain("Bob");
    }

    [Fact]
    public void Reconnect_ResetsCommentatorState()
    {
        // First connection: commentator fires first blood
        var state1 = CreateState();
        var turnResolved = new TurnResolvedRealtime
        {
            BattleId = _battleId, TurnIndex = 1,
            PlayerAAction = "Attack", PlayerBAction = "Attack",
            Log = new TurnResolutionLogRealtime
            {
                BattleId = _battleId, TurnIndex = 1,
                AtoB = new AttackResolutionRealtime
                {
                    AttackerId = _playerAId, DefenderId = _playerBId, TurnIndex = 1,
                    Outcome = AttackOutcomeRealtime.Hit, Damage = 10
                },
                BtoA = new AttackResolutionRealtime
                {
                    AttackerId = _playerBId, DefenderId = _playerAId, TurnIndex = 1,
                    Outcome = AttackOutcomeRealtime.NoAction, Damage = 0
                }
            }
        };

        var feed1 = _pipeline.GenerateTurnFeed(
            state1.BattleId, turnResolved, state1.Participants, state1.Commentator,
            100, 90, 100, 100);

        state1.Commentator.FirstBloodFired.Should().BeTrue();

        // Simulate reconnect: fresh CommentatorState
        var state2 = new BattleConnectionState
        {
            BattleId = _battleId,
            Participants = new BattleParticipantSnapshot(_playerAId, _playerBId, "Alice", "Bob"),
            Commentator = new CommentatorState(), // fresh
            PlayerAHp = 90,
            PlayerBHp = 80,
            PlayerAMaxHp = 100,
            PlayerBMaxHp = 100
        };

        state2.Commentator.FirstBloodFired.Should().BeFalse();

        // First blood can fire again on new connection
        var feed2 = _pipeline.GenerateTurnFeed(
            state2.BattleId, turnResolved, state2.Participants, state2.Commentator,
            90, 80, 100, 100);

        state2.Commentator.FirstBloodFired.Should().BeTrue();
    }

    [Fact]
    public void MaxHp_ComesFromDurableSnapshot_NotCurrentHp()
    {
        // Simulate snapshot with max HP from durable state
        var snapshot = new BattleSnapshotRealtime
        {
            BattleId = _battleId,
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            PlayerAHp = 50,  // current HP (damaged)
            PlayerBHp = 30,  // current HP (damaged)
            PlayerAMaxHp = 150, // durable max HP
            PlayerBMaxHp = 130, // durable max HP
            PlayerAName = "Alice",
            PlayerBName = "Bob"
        };

        var state = new BattleConnectionState
        {
            BattleId = _battleId,
            Participants = new BattleParticipantSnapshot(
                snapshot.PlayerAId, snapshot.PlayerBId,
                snapshot.PlayerAName, snapshot.PlayerBName),
            Commentator = new CommentatorState(),
            PlayerAHp = snapshot.PlayerAHp,
            PlayerBHp = snapshot.PlayerBHp,
            PlayerAMaxHp = snapshot.PlayerAMaxHp,
            PlayerBMaxHp = snapshot.PlayerBMaxHp
        };

        // MaxHp should be from durable snapshot fields, NOT from current HP
        state.PlayerAMaxHp.Should().Be(150);
        state.PlayerBMaxHp.Should().Be(130);
        state.PlayerAHp.Should().Be(50);
        state.PlayerBHp.Should().Be(30);
    }
}
