using System.Text.Json;
using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Matchmaking.Contracts;
using Kombats.Players.Contracts;
using Xunit;

namespace Kombats.Integration.Tests;

/// <summary>
/// I-05: Contract Serialization Comprehensive Test.
/// Verifies that all integration contracts serialize/deserialize correctly
/// with all fields including Version. This ensures cross-service contract
/// compatibility — any serialization mismatch will break the event flow.
/// </summary>
public sealed class ContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // --- PlayerCombatProfileChanged ---

    [Fact]
    public void PlayerCombatProfileChanged_RoundTrips_AllFields()
    {
        var original = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = "TestHero",
            Level = 10,
            Strength = 15,
            Agility = 12,
            Intuition = 8,
            Vitality = 20,
            IsReady = true,
            Revision = 5,
            OccurredAt = new DateTimeOffset(2026, 4, 7, 12, 0, 0, TimeSpan.Zero),
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be(original.MessageId);
        deserialized.IdentityId.Should().Be(original.IdentityId);
        deserialized.CharacterId.Should().Be(original.CharacterId);
        deserialized.Name.Should().Be("TestHero");
        deserialized.Level.Should().Be(10);
        deserialized.Strength.Should().Be(15);
        deserialized.Agility.Should().Be(12);
        deserialized.Intuition.Should().Be(8);
        deserialized.Vitality.Should().Be(20);
        deserialized.IsReady.Should().BeTrue();
        deserialized.Revision.Should().Be(5);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
        deserialized.Version.Should().Be(1);
    }

    [Fact]
    public void PlayerCombatProfileChanged_NullName_RoundTrips()
    {
        var original = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = null,
            Level = 1,
            Strength = 5,
            Agility = 5,
            Intuition = 5,
            Vitality = 5,
            IsReady = false,
            Revision = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().BeNull();
        deserialized.IsReady.Should().BeFalse();
    }

    [Fact]
    public void PlayerCombatProfileChanged_DefaultVersion_Is1()
    {
        var msg = new PlayerCombatProfileChanged();
        msg.Version.Should().Be(1);
    }

    // --- CreateBattle (command) ---

    [Fact]
    public void CreateBattle_RoundTrips_AllFields()
    {
        var original = new CreateBattle
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            RequestedAt = new DateTimeOffset(2026, 4, 7, 12, 0, 0, TimeSpan.Zero),
            PlayerA = new BattleParticipantSnapshot
            {
                IdentityId = Guid.NewGuid(),
                CharacterId = Guid.NewGuid(),
                Name = "PlayerA",
                Level = 10,
                Strength = 15,
                Agility = 12,
                Intuition = 8,
                Vitality = 20
            },
            PlayerB = new BattleParticipantSnapshot
            {
                IdentityId = Guid.NewGuid(),
                CharacterId = Guid.NewGuid(),
                Name = "PlayerB",
                Level = 9,
                Strength = 10,
                Agility = 18,
                Intuition = 12,
                Vitality = 15
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CreateBattle>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.BattleId.Should().Be(original.BattleId);
        deserialized.MatchId.Should().Be(original.MatchId);
        deserialized.RequestedAt.Should().Be(original.RequestedAt);
        deserialized.PlayerA.IdentityId.Should().Be(original.PlayerA.IdentityId);
        deserialized.PlayerA.CharacterId.Should().Be(original.PlayerA.CharacterId);
        deserialized.PlayerA.Name.Should().Be("PlayerA");
        deserialized.PlayerA.Level.Should().Be(10);
        deserialized.PlayerA.Strength.Should().Be(15);
        deserialized.PlayerA.Agility.Should().Be(12);
        deserialized.PlayerA.Intuition.Should().Be(8);
        deserialized.PlayerA.Vitality.Should().Be(20);
        deserialized.PlayerB.IdentityId.Should().Be(original.PlayerB.IdentityId);
        deserialized.PlayerB.Name.Should().Be("PlayerB");
    }

    [Fact]
    public void BattleParticipantSnapshot_NullName_RoundTrips()
    {
        var original = new BattleParticipantSnapshot
        {
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = null,
            Level = 1,
            Strength = 5,
            Agility = 5,
            Intuition = 5,
            Vitality = 5
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BattleParticipantSnapshot>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().BeNull();
    }

    // --- BattleCreated ---

    [Fact]
    public void BattleCreated_RoundTrips_AllFields()
    {
        var original = new BattleCreated
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 4, 7, 12, 0, 0, TimeSpan.Zero),
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BattleCreated>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.BattleId.Should().Be(original.BattleId);
        deserialized.MatchId.Should().Be(original.MatchId);
        deserialized.PlayerAId.Should().Be(original.PlayerAId);
        deserialized.PlayerBId.Should().Be(original.PlayerBId);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
        deserialized.Version.Should().Be(1);
    }

    [Fact]
    public void BattleCreated_DefaultVersion_Is1()
    {
        var msg = new BattleCreated();
        msg.Version.Should().Be(1);
    }

    // --- BattleCompleted ---

    [Fact]
    public void BattleCompleted_WithWinner_RoundTrips_AllFields()
    {
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();

        var original = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = winnerId,
            PlayerBIdentityId = loserId,
            WinnerIdentityId = winnerId,
            LoserIdentityId = loserId,
            Reason = BattleEndReason.Normal,
            TurnCount = 10,
            DurationMs = 45000,
            RulesetVersion = 1,
            OccurredAt = new DateTimeOffset(2026, 4, 7, 12, 5, 0, TimeSpan.Zero),
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BattleCompleted>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be(original.MessageId);
        deserialized.BattleId.Should().Be(original.BattleId);
        deserialized.MatchId.Should().Be(original.MatchId);
        deserialized.PlayerAIdentityId.Should().Be(winnerId);
        deserialized.PlayerBIdentityId.Should().Be(loserId);
        deserialized.WinnerIdentityId.Should().Be(winnerId);
        deserialized.LoserIdentityId.Should().Be(loserId);
        deserialized.Reason.Should().Be(BattleEndReason.Normal);
        deserialized.TurnCount.Should().Be(10);
        deserialized.DurationMs.Should().Be(45000);
        deserialized.RulesetVersion.Should().Be(1);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
        deserialized.Version.Should().Be(1);
    }

    [Fact]
    public void BattleCompleted_Draw_NullWinnerLoser_RoundTrips()
    {
        var original = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = Guid.NewGuid(),
            PlayerBIdentityId = Guid.NewGuid(),
            WinnerIdentityId = null,
            LoserIdentityId = null,
            Reason = BattleEndReason.DoubleForfeit,
            TurnCount = 20,
            DurationMs = 60000,
            RulesetVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BattleCompleted>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.WinnerIdentityId.Should().BeNull();
        deserialized.LoserIdentityId.Should().BeNull();
        deserialized.Reason.Should().Be(BattleEndReason.DoubleForfeit);
    }

    [Fact]
    public void BattleCompleted_DefaultVersion_Is1()
    {
        var msg = new BattleCompleted();
        msg.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(BattleEndReason.Normal)]
    [InlineData(BattleEndReason.DoubleForfeit)]
    [InlineData(BattleEndReason.Timeout)]
    [InlineData(BattleEndReason.Cancelled)]
    [InlineData(BattleEndReason.AdminForced)]
    [InlineData(BattleEndReason.SystemError)]
    public void BattleCompleted_AllEndReasons_SerializeCorrectly(BattleEndReason reason)
    {
        var original = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = Guid.NewGuid(),
            PlayerBIdentityId = Guid.NewGuid(),
            Reason = reason,
            TurnCount = 1,
            DurationMs = 1000,
            RulesetVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BattleCompleted>(json, JsonOptions);

        deserialized!.Reason.Should().Be(reason);
    }

    // --- MatchCreated ---

    [Fact]
    public void MatchCreated_RoundTrips_AllFields()
    {
        var original = new MatchCreated
        {
            MessageId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = Guid.NewGuid(),
            PlayerBIdentityId = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 4, 7, 12, 0, 0, TimeSpan.Zero),
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MatchCreated>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be(original.MessageId);
        deserialized.MatchId.Should().Be(original.MatchId);
        deserialized.PlayerAIdentityId.Should().Be(original.PlayerAIdentityId);
        deserialized.PlayerBIdentityId.Should().Be(original.PlayerBIdentityId);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
        deserialized.Version.Should().Be(1);
    }

    [Fact]
    public void MatchCreated_DefaultVersion_Is1()
    {
        var msg = new MatchCreated();
        msg.Version.Should().Be(1);
    }

    // --- MatchCompleted ---

    [Fact]
    public void MatchCompleted_WithWinner_RoundTrips_AllFields()
    {
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();

        var original = new MatchCompleted
        {
            MessageId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = winnerId,
            PlayerBIdentityId = loserId,
            WinnerIdentityId = winnerId,
            LoserIdentityId = loserId,
            OccurredAt = new DateTimeOffset(2026, 4, 7, 12, 0, 0, TimeSpan.Zero),
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MatchCompleted>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be(original.MessageId);
        deserialized.MatchId.Should().Be(original.MatchId);
        deserialized.PlayerAIdentityId.Should().Be(winnerId);
        deserialized.PlayerBIdentityId.Should().Be(loserId);
        deserialized.WinnerIdentityId.Should().Be(winnerId);
        deserialized.LoserIdentityId.Should().Be(loserId);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
        deserialized.Version.Should().Be(1);
    }

    [Fact]
    public void MatchCompleted_Draw_NullWinnerLoser_RoundTrips()
    {
        var original = new MatchCompleted
        {
            MessageId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = Guid.NewGuid(),
            PlayerBIdentityId = Guid.NewGuid(),
            WinnerIdentityId = null,
            LoserIdentityId = null,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MatchCompleted>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.WinnerIdentityId.Should().BeNull();
        deserialized.LoserIdentityId.Should().BeNull();
    }

    [Fact]
    public void MatchCompleted_DefaultVersion_Is1()
    {
        var msg = new MatchCompleted();
        msg.Version.Should().Be(1);
    }

    // --- Cross-contract field alignment ---

    [Fact]
    public void BattleCompleted_Fields_AlignWith_MatchCompleted_ConsumerExpectations()
    {
        // Matchmaking's BattleCompletedConsumer reads BattleId, MatchId, Reason,
        // PlayerAIdentityId, PlayerBIdentityId from BattleCompleted.
        // Verify these fields exist and are settable.
        var bc = new BattleCompleted
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAIdentityId = Guid.NewGuid(),
            PlayerBIdentityId = Guid.NewGuid(),
            Reason = BattleEndReason.Normal
        };

        bc.BattleId.Should().NotBeEmpty();
        bc.MatchId.Should().NotBeEmpty();
        bc.PlayerAIdentityId.Should().NotBeEmpty();
        bc.PlayerBIdentityId.Should().NotBeEmpty();
    }

    [Fact]
    public void BattleCompleted_Fields_AlignWith_Players_ConsumerExpectations()
    {
        // Players' BattleCompletedConsumer reads MessageId, WinnerIdentityId,
        // LoserIdentityId, Reason from BattleCompleted.
        var bc = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            WinnerIdentityId = Guid.NewGuid(),
            LoserIdentityId = Guid.NewGuid(),
            Reason = BattleEndReason.Normal
        };

        bc.MessageId.Should().NotBeEmpty();
        bc.WinnerIdentityId.Should().NotBeNull();
        bc.LoserIdentityId.Should().NotBeNull();
    }

    [Fact]
    public void PlayerCombatProfileChanged_Fields_AlignWith_Matchmaking_ConsumerExpectations()
    {
        // Matchmaking's PlayerCombatProfileChangedConsumer reads all profile fields
        // and creates a PlayerCombatProfile application model.
        var pcp = new PlayerCombatProfileChanged
        {
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = "Hero",
            Level = 5,
            Strength = 10,
            Agility = 8,
            Intuition = 6,
            Vitality = 12,
            IsReady = true,
            Revision = 3,
            OccurredAt = DateTimeOffset.UtcNow
        };

        pcp.IdentityId.Should().NotBeEmpty();
        pcp.CharacterId.Should().NotBeEmpty();
        pcp.IsReady.Should().BeTrue();
        pcp.Revision.Should().Be(3);
    }

    [Fact]
    public void CreateBattle_Fields_AlignWith_Battle_ConsumerExpectations()
    {
        // Battle's CreateBattleConsumer reads BattleId, MatchId, PlayerA, PlayerB.
        // PlayerA.Vitality maps to Stamina in Battle domain.
        var cb = new CreateBattle
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            RequestedAt = DateTimeOffset.UtcNow,
            PlayerA = new BattleParticipantSnapshot
            {
                IdentityId = Guid.NewGuid(),
                CharacterId = Guid.NewGuid(),
                Name = "A",
                Level = 5,
                Strength = 10,
                Agility = 8,
                Intuition = 6,
                Vitality = 12
            },
            PlayerB = new BattleParticipantSnapshot
            {
                IdentityId = Guid.NewGuid(),
                CharacterId = Guid.NewGuid(),
                Name = "B",
                Level = 5,
                Strength = 8,
                Agility = 10,
                Intuition = 8,
                Vitality = 10
            }
        };

        cb.PlayerA.Vitality.Should().Be(12, "Vitality in contract maps to Stamina in Battle domain");
        cb.PlayerB.Vitality.Should().Be(10);
    }

    [Fact]
    public void BattleCreated_Fields_AlignWith_Matchmaking_ConsumerExpectations()
    {
        // Matchmaking's BattleCreatedConsumer reads BattleId, MatchId.
        var bc = new BattleCreated
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow
        };

        bc.BattleId.Should().NotBeEmpty();
        bc.MatchId.Should().NotBeEmpty();
    }

    // --- Additive compatibility: extra fields ignored ---

    [Fact]
    public void BattleCompleted_ExtraJsonFields_AreIgnoredOnDeserialization()
    {
        // Simulate a future version with extra fields — ensure backward compatibility
        var json = """
        {
            "messageId": "00000000-0000-0000-0000-000000000001",
            "battleId": "00000000-0000-0000-0000-000000000002",
            "matchId": "00000000-0000-0000-0000-000000000003",
            "playerAIdentityId": "00000000-0000-0000-0000-000000000004",
            "playerBIdentityId": "00000000-0000-0000-0000-000000000005",
            "winnerIdentityId": null,
            "loserIdentityId": null,
            "reason": 1,
            "turnCount": 5,
            "durationMs": 10000,
            "rulesetVersion": 1,
            "occurredAt": "2026-04-07T12:00:00+00:00",
            "version": 1,
            "futureField": "should be ignored"
        }
        """;

        var deserialized = JsonSerializer.Deserialize<BattleCompleted>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Reason.Should().Be(BattleEndReason.DoubleForfeit);
        deserialized.TurnCount.Should().Be(5);
        deserialized.Version.Should().Be(1);
    }
}
