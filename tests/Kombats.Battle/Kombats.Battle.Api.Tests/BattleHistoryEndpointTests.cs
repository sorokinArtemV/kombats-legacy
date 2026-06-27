using FluentAssertions;
using Kombats.Battle.Api.Endpoints;
using Kombats.Battle.Api.Endpoints.BattleHistory;
using Xunit;

namespace Kombats.Battle.Api.Tests;

public sealed class BattleHistoryEndpointTests
{
    [Fact]
    public void GetBattleHistoryEndpoint_ImplementsIEndpoint()
    {
        typeof(GetBattleHistoryEndpoint).Should().Implement<IEndpoint>();
    }

    [Fact]
    public void BattleHistoryResponse_HasAllExpectedFields()
    {
        var response = new BattleHistoryResponse
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 150,
            PlayerBMaxHp = 130,
            State = "Ended",
            EndReason = "Normal",
            WinnerPlayerId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
            Turns =
            [
                new TurnHistoryResponse
                {
                    TurnIndex = 1,
                    AtoBAttackZone = "Head",
                    AtoBDefenderBlockPrimary = "Chest",
                    AtoBDefenderBlockSecondary = "Belly",
                    AtoBWasBlocked = false,
                    AtoBWasCrit = false,
                    AtoBOutcome = "Hit",
                    AtoBDamage = 15,
                    BtoAAttackZone = "Chest",
                    BtoADefenderBlockPrimary = "Head",
                    BtoADefenderBlockSecondary = "Legs",
                    BtoAWasBlocked = true,
                    BtoAWasCrit = false,
                    BtoAOutcome = "Blocked",
                    BtoADamage = 0,
                    PlayerAHpAfter = 100,
                    PlayerBHpAfter = 85,
                    ResolvedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        response.PlayerAName.Should().Be("Alice");
        response.PlayerBName.Should().Be("Bob");
        response.PlayerAMaxHp.Should().Be(150);
        response.PlayerBMaxHp.Should().Be(130);
        response.Turns.Should().HaveCount(1);
        response.Turns[0].AtoBOutcome.Should().Be("Hit");
        response.Turns[0].BtoAOutcome.Should().Be("Blocked");
    }

    [Fact]
    public void TurnHistoryResponse_DefaultValues()
    {
        var turn = new TurnHistoryResponse();

        turn.AtoBOutcome.Should().Be(string.Empty);
        turn.BtoAOutcome.Should().Be(string.Empty);
        turn.AtoBDamage.Should().Be(0);
        turn.BtoADamage.Should().Be(0);
    }

    [Fact]
    public void BattleHistoryResponse_EmptyTurns_DefaultsToEmptyArray()
    {
        var response = new BattleHistoryResponse();

        response.Turns.Should().BeEmpty();
    }
}
