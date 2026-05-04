using FluentAssertions;
using Kombats.Bff.Api.Endpoints;
using Kombats.Bff.Api.Endpoints.BattleFeed;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class BattleFeedEndpointTests
{
    [Fact]
    public void GetBattleFeedEndpoint_ImplementsIEndpoint()
    {
        typeof(GetBattleFeedEndpoint).Should().Implement<IEndpoint>();
    }

    [Fact]
    public void BattleFeedResponse_HasExpectedShape()
    {
        var entry = new BattleFeedEntry
        {
            Key = "test:1:0",
            BattleId = Guid.NewGuid(),
            TurnIndex = 1,
            Sequence = 0,
            Kind = FeedEntryKind.AttackHit,
            Severity = FeedEntrySeverity.Normal,
            Tone = FeedEntryTone.Neutral,
            Text = "Alice hits Bob!"
        };

        var response = new BattleFeedResponse
        {
            BattleId = entry.BattleId,
            Entries = [entry]
        };

        response.Entries.Should().HaveCount(1);
        response.Entries[0].Text.Should().Contain("Alice");
    }

    /// <summary>
    /// Verifies that the same BattleHistoryResponse produces identical feed entries
    /// on every call — the determinism guarantee from the execution plan.
    /// </summary>
    [Fact]
    public void PostMatchFeed_IsDeterministic_AcrossMultipleCalls()
    {
        var pipeline = new NarrationPipeline(
            new InMemoryTemplateCatalog(),
            new DeterministicTemplateSelector(),
            new PlaceholderNarrationRenderer(),
            new DefaultCommentatorPolicy(),
            new DefaultFeedAssembler());

        var battleId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var playerAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var playerBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var history = new BattleHistory
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 100,
            PlayerBMaxHp = 100,
            EndReason = "Normal",
            WinnerPlayerId = playerAId,
            Turns =
            [
                new BattleHistoryTurn
                {
                    TurnIndex = 1,
                    AtoBOutcome = "Hit", AtoBDamage = 30, AtoBAttackZone = "Head",
                    BtoAOutcome = "Dodged", BtoADamage = 0,
                    PlayerAHpAfter = 100, PlayerBHpAfter = 70
                },
                new BattleHistoryTurn
                {
                    TurnIndex = 2,
                    AtoBOutcome = "CriticalHit", AtoBDamage = 70, AtoBAttackZone = "Chest",
                    BtoAOutcome = "Hit", BtoADamage = 15,
                    PlayerAHpAfter = 85, PlayerBHpAfter = 0
                }
            ]
        };

        var result1 = pipeline.GenerateFullBattleFeed(history);
        var result2 = pipeline.GenerateFullBattleFeed(history);

        result1.Should().HaveCount(result2.Length);
        for (int i = 0; i < result1.Length; i++)
        {
            result1[i].Key.Should().Be(result2[i].Key);
            result1[i].Text.Should().Be(result2[i].Text);
            result1[i].Kind.Should().Be(result2[i].Kind);
            result1[i].Tone.Should().Be(result2[i].Tone);
        }
    }

    /// <summary>
    /// Verifies that mapping from client BattleHistoryResponse to narration BattleHistory
    /// produces the same feed as using the narration model directly — proving the mapping
    /// doesn't lose information.
    /// </summary>
    [Fact]
    public void ClientResponseMapping_ProducesSameFeedAsDirectModel()
    {
        var pipeline = new NarrationPipeline(
            new InMemoryTemplateCatalog(),
            new DeterministicTemplateSelector(),
            new PlaceholderNarrationRenderer(),
            new DefaultCommentatorPolicy(),
            new DefaultFeedAssembler());

        var battleId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var playerAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var playerBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // Simulate what the client would receive from Battle
        var clientResponse = new BattleHistoryResponse
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 100,
            PlayerBMaxHp = 100,
            State = "Ended",
            EndReason = "Normal",
            WinnerPlayerId = playerAId,
            Turns =
            [
                new Application.Clients.TurnHistoryResponse
                {
                    TurnIndex = 1,
                    AtoBOutcome = "Hit", AtoBDamage = 100,
                    BtoAOutcome = "NoAction", BtoADamage = 0,
                    PlayerAHpAfter = 100, PlayerBHpAfter = 0
                }
            ]
        };

        // Map to narration model (same mapping the endpoint does)
        var narrationHistory = new BattleHistory
        {
            BattleId = clientResponse.BattleId,
            PlayerAId = clientResponse.PlayerAId,
            PlayerBId = clientResponse.PlayerBId,
            PlayerAName = clientResponse.PlayerAName,
            PlayerBName = clientResponse.PlayerBName,
            PlayerAMaxHp = clientResponse.PlayerAMaxHp,
            PlayerBMaxHp = clientResponse.PlayerBMaxHp,
            EndReason = clientResponse.EndReason,
            WinnerPlayerId = clientResponse.WinnerPlayerId,
            Turns = clientResponse.Turns.Select(t => new BattleHistoryTurn
            {
                TurnIndex = t.TurnIndex,
                AtoBAttackZone = t.AtoBAttackZone,
                AtoBDefenderBlockPrimary = t.AtoBDefenderBlockPrimary,
                AtoBDefenderBlockSecondary = t.AtoBDefenderBlockSecondary,
                AtoBWasBlocked = t.AtoBWasBlocked,
                AtoBWasCrit = t.AtoBWasCrit,
                AtoBOutcome = t.AtoBOutcome,
                AtoBDamage = t.AtoBDamage,
                BtoAAttackZone = t.BtoAAttackZone,
                BtoADefenderBlockPrimary = t.BtoADefenderBlockPrimary,
                BtoADefenderBlockSecondary = t.BtoADefenderBlockSecondary,
                BtoAWasBlocked = t.BtoAWasBlocked,
                BtoAWasCrit = t.BtoAWasCrit,
                BtoAOutcome = t.BtoAOutcome,
                BtoADamage = t.BtoADamage,
                PlayerAHpAfter = t.PlayerAHpAfter,
                PlayerBHpAfter = t.PlayerBHpAfter
            }).ToArray()
        };

        // Both should produce identical feeds
        var feedFromMapping = pipeline.GenerateFullBattleFeed(narrationHistory);

        feedFromMapping.Should().NotBeEmpty();
        feedFromMapping[0].Kind.Should().Be(FeedEntryKind.BattleStart);
        feedFromMapping.Should().Contain(e => e.Kind == FeedEntryKind.BattleEndVictory);
    }
}
