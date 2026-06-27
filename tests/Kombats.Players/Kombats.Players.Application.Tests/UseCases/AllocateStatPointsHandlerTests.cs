using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.UseCases.AllocateStatPoints;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Players.Application.Tests.UseCases;

public sealed class AllocateStatPointsHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly ICombatProfilePublisher _publisher = Substitute.For<ICombatProfilePublisher>();
    private readonly AllocateStatPointsHandler _handler;

    public AllocateStatPointsHandlerTests()
    {
        _handler = new AllocateStatPointsHandler(
            _uow, _characters, _publisher, NullLogger<AllocateStatPointsHandler>.Instance);
    }

    private static Character CreateNamedCharacter(Guid? identityId = null)
    {
        var c = Character.CreateDraft(identityId ?? Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.SetNameOnce("TestChar", DateTimeOffset.UtcNow);
        return c;
    }

    [Fact]
    public async Task Allocates_points_successfully()
    {
        var character = CreateNamedCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        var revision = character.Revision;

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, revision, 1, 1, 1, 0),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Strength.Should().Be(4);
        result.Value.Agility.Should().Be(4);
        result.Value.Intuition.Should().Be(4);
        result.Value.Vitality.Should().Be(3);
        result.Value.UnspentPoints.Should().Be(0);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(Arg.Any<PlayerCombatProfileChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_validation_error_for_invalid_revision()
    {
        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(Guid.NewGuid(), 0, 1, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("AllocateStatPoints.ExpectedRevisionInvalid");
    }

    [Fact]
    public async Task Returns_not_found_when_character_missing()
    {
        _characters.GetByIdentityIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(Guid.NewGuid(), 1, 1, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Returns_conflict_on_revision_mismatch()
    {
        var character = CreateNamedCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, 999, 1, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("AllocateStatPoints.RevisionMismatch");
    }

    [Fact]
    public async Task Returns_conflict_when_character_in_draft_state()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), DateTimeOffset.UtcNow);
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, character.Revision, 1, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Returns_validation_error_for_negative_points()
    {
        var character = CreateNamedCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, character.Revision, -1, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_validation_error_when_exceeding_unspent()
    {
        var character = CreateNamedCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, character.Revision, 10, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_validation_error_for_zero_total()
    {
        var character = CreateNamedCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, character.Revision, 0, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_conflict_on_concurrency_exception()
    {
        var character = CreateNamedCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException(new Exception()));

        var result = await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, character.Revision, 1, 0, 0, 0),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("AllocateStatPoints.ConcurrentUpdate");
    }

    [Fact]
    public async Task Transitions_named_to_ready()
    {
        var character = CreateNamedCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        await _handler.HandleAsync(
            new AllocateStatPointsCommand(character.IdentityId, character.Revision, 1, 0, 0, 0),
            CancellationToken.None);

        character.OnboardingState.Should().Be(OnboardingState.Ready);
    }
}
