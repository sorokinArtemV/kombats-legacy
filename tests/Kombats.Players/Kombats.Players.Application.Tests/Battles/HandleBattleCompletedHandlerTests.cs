using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.Battles;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Domain.Progression;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kombats.Players.Application.Tests.Battles;

public sealed class HandleBattleCompletedHandlerTests
{
    private readonly IInboxRepository _inbox = Substitute.For<IInboxRepository>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly ILevelingConfigProvider _levelingProvider = Substitute.For<ILevelingConfigProvider>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICombatProfilePublisher _publisher = Substitute.For<ICombatProfilePublisher>();
    private readonly HandleBattleCompletedHandler _handler;

    public HandleBattleCompletedHandlerTests()
    {
        _levelingProvider.Get().Returns(new LevelingConfig(100));
        _levelingProvider.GetCurrentVersion().Returns(1);
        _handler = new HandleBattleCompletedHandler(
            _inbox, _characters, _levelingProvider, _uow, _publisher,
            NullLogger<HandleBattleCompletedHandler>.Instance);
    }

    private static Character CreateReadyCharacter(Guid identityId)
    {
        var c = Character.CreateDraft(identityId, DateTimeOffset.UtcNow);
        c.SetNameOnce("Char" + identityId.ToString()[..4], DateTimeOffset.UtcNow);
        c.AllocatePoints(1, 1, 1, 0, DateTimeOffset.UtcNow);
        return c;
    }

    [Fact]
    public async Task Awards_xp_and_records_win_loss()
    {
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();
        var winner = CreateReadyCharacter(winnerId);
        var loser = CreateReadyCharacter(loserId);

        _inbox.IsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _characters.GetByIdentityIdAsync(winnerId, Arg.Any<CancellationToken>()).Returns(winner);
        _characters.GetByIdentityIdAsync(loserId, Arg.Any<CancellationToken>()).Returns(loser);

        var command = new HandleBattleCompletedCommand(Guid.NewGuid(), Guid.NewGuid(), winnerId, loserId, "NormalVictory");
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        winner.TotalXp.Should().Be(10);
        winner.Wins.Should().Be(1);
        loser.TotalXp.Should().Be(5);
        loser.Losses.Should().Be(1);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _publisher.Received(2).PublishAsync(Arg.Any<PlayerCombatProfileChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_xp_on_draw_with_null_winner_loser()
    {
        _inbox.IsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var command = new HandleBattleCompletedCommand(Guid.NewGuid(), Guid.NewGuid(), null, null, "DoubleForfeit");
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _characters.DidNotReceive().GetByIdentityIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<PlayerCombatProfileChanged>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_success_immediately_for_already_processed_message()
    {
        var messageId = Guid.NewGuid();
        _inbox.IsProcessedAsync(messageId, Arg.Any<CancellationToken>()).Returns(true);

        var command = new HandleBattleCompletedCommand(messageId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "NormalVictory");
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _characters.DidNotReceive().GetByIdentityIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_not_found_when_winner_missing()
    {
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();
        _inbox.IsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _characters.GetByIdentityIdAsync(winnerId, Arg.Any<CancellationToken>()).Returns((Character?)null);

        var command = new HandleBattleCompletedCommand(Guid.NewGuid(), Guid.NewGuid(), winnerId, loserId, "NormalVictory");
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("HandleBattleCompleted.WinnerNotFound");
    }

    [Fact]
    public async Task Returns_not_found_when_loser_missing()
    {
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();
        _inbox.IsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _characters.GetByIdentityIdAsync(winnerId, Arg.Any<CancellationToken>())
            .Returns(CreateReadyCharacter(winnerId));
        _characters.GetByIdentityIdAsync(loserId, Arg.Any<CancellationToken>()).Returns((Character?)null);

        var command = new HandleBattleCompletedCommand(Guid.NewGuid(), Guid.NewGuid(), winnerId, loserId, "NormalVictory");
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("HandleBattleCompleted.LoserNotFound");
    }

    [Fact]
    public async Task Marks_message_as_processed()
    {
        _inbox.IsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var messageId = Guid.NewGuid();
        var command = new HandleBattleCompletedCommand(messageId, Guid.NewGuid(), null, null, "Draw");
        await _handler.HandleAsync(command, CancellationToken.None);

        await _inbox.Received(1).AddProcessedAsync(messageId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publishes_profile_for_both_winner_and_loser()
    {
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();
        _inbox.IsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _characters.GetByIdentityIdAsync(winnerId, Arg.Any<CancellationToken>())
            .Returns(CreateReadyCharacter(winnerId));
        _characters.GetByIdentityIdAsync(loserId, Arg.Any<CancellationToken>())
            .Returns(CreateReadyCharacter(loserId));

        var published = new List<PlayerCombatProfileChanged>();
        await _publisher.PublishAsync(Arg.Do<PlayerCombatProfileChanged>(e => published.Add(e)), Arg.Any<CancellationToken>());

        var command = new HandleBattleCompletedCommand(Guid.NewGuid(), Guid.NewGuid(), winnerId, loserId, "NormalVictory");
        await _handler.HandleAsync(command, CancellationToken.None);

        published.Should().HaveCount(2);
        published.Should().Contain(e => e.IdentityId == winnerId);
        published.Should().Contain(e => e.IdentityId == loserId);
    }
}
