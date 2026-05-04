using FluentAssertions;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.UseCases.GetBattleHistory;
using NSubstitute;
using Xunit;

namespace Kombats.Battle.Application.Tests.GetBattleHistory;

public class GetBattleHistoryHandlerTests
{
    private readonly IBattleHistoryRepository _repository = Substitute.For<IBattleHistoryRepository>();
    private readonly GetBattleHistoryHandler _handler;

    private readonly Guid _battleId = Guid.NewGuid();
    private readonly Guid _playerAId = Guid.NewGuid();
    private readonly Guid _playerBId = Guid.NewGuid();

    public GetBattleHistoryHandlerTests()
    {
        _handler = new GetBattleHistoryHandler(_repository);
    }

    private BattleHistoryResult CreateResult(int turnCount = 0)
    {
        var turns = Enumerable.Range(1, turnCount).Select(i => new TurnHistoryItem
        {
            TurnIndex = i,
            AtoBOutcome = "Hit",
            AtoBDamage = 10,
            BtoAOutcome = "Dodged",
            BtoADamage = 0,
            PlayerAHpAfter = 100,
            PlayerBHpAfter = 100 - i * 10,
            ResolvedAt = DateTimeOffset.UtcNow
        }).ToArray();

        return new BattleHistoryResult
        {
            BattleId = _battleId,
            MatchId = Guid.NewGuid(),
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 150,
            PlayerBMaxHp = 130,
            State = "ArenaOpen",
            CreatedAt = DateTimeOffset.UtcNow,
            Turns = turns
        };
    }

    [Fact]
    public async Task ValidParticipant_ReturnsResult()
    {
        var result = CreateResult(turnCount: 3);
        _repository.GetBattleHistoryAsync(_battleId, Arg.Any<CancellationToken>()).Returns(result);

        var query = new GetBattleHistoryQuery(_battleId, _playerAId);
        var actual = await _handler.HandleAsync(query);

        actual.Should().NotBeNull();
        actual!.BattleId.Should().Be(_battleId);
        actual.PlayerAName.Should().Be("Alice");
        actual.PlayerBName.Should().Be("Bob");
        actual.Turns.Should().HaveCount(3);
    }

    [Fact]
    public async Task PlayerB_IsParticipant_ReturnsResult()
    {
        var result = CreateResult();
        _repository.GetBattleHistoryAsync(_battleId, Arg.Any<CancellationToken>()).Returns(result);

        var query = new GetBattleHistoryQuery(_battleId, _playerBId);
        var actual = await _handler.HandleAsync(query);

        actual.Should().NotBeNull();
    }

    [Fact]
    public async Task NonParticipant_Throws()
    {
        var result = CreateResult();
        _repository.GetBattleHistoryAsync(_battleId, Arg.Any<CancellationToken>()).Returns(result);

        var query = new GetBattleHistoryQuery(_battleId, Guid.NewGuid());
        var act = () => _handler.HandleAsync(query);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task BattleNotFound_ReturnsNull()
    {
        _repository.GetBattleHistoryAsync(_battleId, Arg.Any<CancellationToken>()).Returns((BattleHistoryResult?)null);

        var query = new GetBattleHistoryQuery(_battleId, _playerAId);
        var actual = await _handler.HandleAsync(query);

        actual.Should().BeNull();
    }

    [Fact]
    public async Task EndedBattle_WithTurns_ReturnsCorrectData()
    {
        var result = CreateResult(turnCount: 5) with
        {
            State = "Ended",
            EndReason = "Normal",
            WinnerPlayerId = _playerAId,
            EndedAt = DateTimeOffset.UtcNow
        };
        _repository.GetBattleHistoryAsync(_battleId, Arg.Any<CancellationToken>()).Returns(result);

        var query = new GetBattleHistoryQuery(_battleId, _playerAId);
        var actual = await _handler.HandleAsync(query);

        actual.Should().NotBeNull();
        actual!.State.Should().Be("Ended");
        actual.EndReason.Should().Be("Normal");
        actual.WinnerPlayerId.Should().Be(_playerAId);
        actual.Turns.Should().HaveCount(5);
        actual.Turns[0].TurnIndex.Should().Be(1);
        actual.Turns[4].TurnIndex.Should().Be(5);
    }

    [Fact]
    public async Task InProgressBattle_EmptyTurns_ReturnsEmptyArray()
    {
        var result = CreateResult(turnCount: 0);
        _repository.GetBattleHistoryAsync(_battleId, Arg.Any<CancellationToken>()).Returns(result);

        var query = new GetBattleHistoryQuery(_battleId, _playerAId);
        var actual = await _handler.HandleAsync(query);

        actual.Should().NotBeNull();
        actual!.Turns.Should().BeEmpty();
    }
}
