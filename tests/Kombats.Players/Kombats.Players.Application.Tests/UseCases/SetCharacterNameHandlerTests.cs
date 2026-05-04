using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.UseCases.SetCharacterName;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Players.Application.Tests.UseCases;

public sealed class SetCharacterNameHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly ICombatProfilePublisher _publisher = Substitute.For<ICombatProfilePublisher>();
    private readonly SetCharacterNameHandler _handler;

    public SetCharacterNameHandlerTests()
    {
        _handler = new SetCharacterNameHandler(
            _uow, _characters, _publisher, NullLogger<SetCharacterNameHandler>.Instance);
    }

    private static Character CreateDraftCharacter(Guid? identityId = null)
    {
        return Character.CreateDraft(identityId ?? Guid.NewGuid(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Sets_name_successfully()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _characters.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.HandleAsync(
            new SetCharacterNameCommand(character.IdentityId, "TestName"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("TestName");
        result.Value.State.Should().Be(OnboardingState.Named);
        await _publisher.Received(1).PublishAsync(Arg.Any<PlayerCombatProfileChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_not_found_when_character_missing()
    {
        var identityId = Guid.NewGuid();
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        var result = await _handler.HandleAsync(
            new SetCharacterNameCommand(identityId, "TestName"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Returns_conflict_when_name_taken()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _characters.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.HandleAsync(
            new SetCharacterNameCommand(character.IdentityId, "TakenName"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SetCharacterName.NameTaken");
    }

    [Fact]
    public async Task Returns_validation_error_for_short_name()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _characters.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.HandleAsync(
            new SetCharacterNameCommand(character.IdentityId, "Ab"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Returns_conflict_on_concurrency_exception()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _characters.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException(new Exception()));

        var result = await _handler.HandleAsync(
            new SetCharacterNameCommand(character.IdentityId, "ValidName"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Returns_conflict_on_unique_name_db_exception()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _characters.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintConflictException(UniqueConflictKind.CharacterName, new Exception()));

        var result = await _handler.HandleAsync(
            new SetCharacterNameCommand(character.IdentityId, "ValidName"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SetCharacterName.NameTaken");
    }

    [Fact]
    public async Task Returns_conflict_when_not_in_draft_state()
    {
        var character = CreateDraftCharacter();
        character.SetNameOnce("AlreadyNamed", DateTimeOffset.UtcNow);
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _characters.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.HandleAsync(
            new SetCharacterNameCommand(character.IdentityId, "NewName"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
