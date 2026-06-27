using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Xunit;

namespace Kombats.Bff.Application.Tests.Clients;

public sealed class BattleClientTests
{
    [Fact]
    public void BattleClient_ImplementsIBattleClient()
    {
        typeof(BattleClient).Should().Implement<IBattleClient>();
    }

    [Fact]
    public void BattleHistoryResponse_DefaultTurnsIsEmptyArray()
    {
        var response = new BattleHistoryResponse();
        response.Turns.Should().BeEmpty();
    }

    [Fact]
    public void BattleHistoryResponse_AllFieldsSettable()
    {
        var battleId = Guid.NewGuid();
        var response = new BattleHistoryResponse
        {
            BattleId = battleId,
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
                    AtoBOutcome = "Hit",
                    AtoBDamage = 15,
                    BtoAOutcome = "Dodged",
                    BtoADamage = 0,
                    PlayerAHpAfter = 100,
                    PlayerBHpAfter = 85,
                    ResolvedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        response.BattleId.Should().Be(battleId);
        response.PlayerAName.Should().Be("Alice");
        response.Turns.Should().HaveCount(1);
    }
}
