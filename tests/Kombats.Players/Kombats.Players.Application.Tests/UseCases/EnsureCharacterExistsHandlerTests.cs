using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.UseCases.EnsureCharacterExists;
using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Players.Application.Tests.UseCases;

public sealed class EnsureCharacterExistsHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly ICombatProfilePublisher _publisher = Substitute.For<ICombatProfilePublisher>();
    private readonly EnsureCharacterExistsHandler _handler;

    public EnsureCharacterExistsHandlerTests()
    {
        _handler = new EnsureCharacterExistsHandler(
            _uow, _characters, _publisher, NullLogger<EnsureCharacterExistsHandler>.Instance);
    }

    [Fact]
    public async Task Returns_existing_character_without_creating()
    {
        var identityId = Guid.NewGuid();
        var existing = Character.CreateDraft(identityId, DateTimeOffset.UtcNow);
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _handler.HandleAsync(new EnsureCharacterExistsCommand(identityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IdentityId.Should().Be(identityId);
        await _characters.DidNotReceive().AddAsync(Arg.Any<Character>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<PlayerCombatProfileChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creates_draft_character_when_none_exists()
    {
        var identityId = Guid.NewGuid();
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        var result = await _handler.HandleAsync(new EnsureCharacterExistsCommand(identityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IdentityId.Should().Be(identityId);
        result.Value.State.Should().Be(OnboardingState.Draft);
        await _characters.Received(1).AddAsync(Arg.Any<Character>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(Arg.Any<PlayerCombatProfileChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publishes_profile_changed_event_on_create()
    {
        var identityId = Guid.NewGuid();
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        PlayerCombatProfileChanged? published = null;
        await _publisher.PublishAsync(Arg.Do<PlayerCombatProfileChanged>(e => published = e), Arg.Any<CancellationToken>());

        await _handler.HandleAsync(new EnsureCharacterExistsCommand(identityId), CancellationToken.None);

        published.Should().NotBeNull();
        published!.IdentityId.Should().Be(identityId);
        published.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task Handles_concurrent_create_by_returning_existing()
    {
        var identityId = Guid.NewGuid();
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns(
                (Character?)null,
                Character.CreateDraft(identityId, DateTimeOffset.UtcNow));

        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintConflictException(UniqueConflictKind.IdentityId, new Exception()));

        var result = await _handler.HandleAsync(new EnsureCharacterExistsCommand(identityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IdentityId.Should().Be(identityId);
    }

    [Fact]
    public async Task Returns_conflict_when_concurrent_create_and_reload_fails()
    {
        var identityId = Guid.NewGuid();
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintConflictException(UniqueConflictKind.IdentityId, new Exception()));

        var result = await _handler.HandleAsync(new EnsureCharacterExistsCommand(identityId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
