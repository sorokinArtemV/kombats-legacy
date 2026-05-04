using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.UseCases.ChangeAvatar;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Players.Application.Tests.UseCases;

public sealed class ChangeAvatarHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly ICombatProfilePublisher _publisher = Substitute.For<ICombatProfilePublisher>();
    private readonly ChangeAvatarHandler _handler;

    private static readonly string AltAvatar =
        AvatarCatalog.AllowedIds.First(id => id != AvatarCatalog.Default);

    public ChangeAvatarHandlerTests()
    {
        _handler = new ChangeAvatarHandler(
            _uow, _characters, _publisher, NullLogger<ChangeAvatarHandler>.Instance);
    }

    private static Character CreateDraftCharacter(Guid? identityId = null) =>
        Character.CreateDraft(identityId ?? Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Changes_avatar_successfully()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        var revision = character.Revision;

        var result = await _handler.HandleAsync(
            new ChangeAvatarCommand(character.IdentityId, revision, AltAvatar),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AvatarId.Should().Be(AltAvatar);
        result.Value.Revision.Should().Be(revision + 1);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(
            Arg.Is<PlayerCombatProfileChanged>(e => e.AvatarId == AltAvatar),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_validation_error_for_invalid_revision()
    {
        var result = await _handler.HandleAsync(
            new ChangeAvatarCommand(Guid.NewGuid(), 0, AltAvatar),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("ChangeAvatar.ExpectedRevisionInvalid");
    }

    [Fact]
    public async Task Returns_not_found_when_character_missing()
    {
        _characters.GetByIdentityIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        var result = await _handler.HandleAsync(
            new ChangeAvatarCommand(Guid.NewGuid(), 1, AltAvatar),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("ChangeAvatar.CharacterNotFound");
    }

    [Fact]
    public async Task Returns_conflict_on_revision_mismatch()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(
            new ChangeAvatarCommand(character.IdentityId, 999, AltAvatar),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("ChangeAvatar.RevisionMismatch");
    }

    [Fact]
    public async Task Returns_validation_error_when_avatar_not_in_catalog()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(
            new ChangeAvatarCommand(character.IdentityId, character.Revision, "not-a-real-avatar"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("ChangeAvatar.InvalidAvatar");
    }

    [Fact]
    public async Task No_op_when_avatar_unchanged_skips_publish_and_save()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        var revision = character.Revision;

        var result = await _handler.HandleAsync(
            new ChangeAvatarCommand(character.IdentityId, revision, character.AvatarId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AvatarId.Should().Be(character.AvatarId);
        result.Value.Revision.Should().Be(revision);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(
            Arg.Any<PlayerCombatProfileChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_conflict_on_concurrency_exception()
    {
        var character = CreateDraftCharacter();
        _characters.GetByIdentityIdAsync(character.IdentityId, Arg.Any<CancellationToken>())
            .Returns(character);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException(new Exception()));

        var result = await _handler.HandleAsync(
            new ChangeAvatarCommand(character.IdentityId, character.Revision, AltAvatar),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("ChangeAvatar.ConcurrentUpdate");
    }
}
