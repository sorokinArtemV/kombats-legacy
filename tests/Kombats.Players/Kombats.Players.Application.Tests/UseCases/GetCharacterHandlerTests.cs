using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.UseCases.GetCharacter;
using Kombats.Players.Domain.Entities;
using NSubstitute;
using Xunit;

namespace Kombats.Players.Application.Tests.UseCases;

public sealed class GetCharacterHandlerTests
{
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly GetCharacterHandler _handler;

    public GetCharacterHandlerTests()
    {
        _handler = new GetCharacterHandler(_characters);
    }

    [Fact]
    public async Task Returns_character_state_when_found()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, DateTimeOffset.UtcNow);
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(new GetCharacterQuery(identityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IdentityId.Should().Be(identityId);
        result.Value.State.Should().Be(OnboardingState.Draft);
    }

    [Fact]
    public async Task Returns_not_found_when_character_missing()
    {
        var identityId = Guid.NewGuid();
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        var result = await _handler.HandleAsync(new GetCharacterQuery(identityId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("GetCharacter.NotProvisioned");
    }

    [Fact]
    public async Task Returns_full_character_snapshot()
    {
        var identityId = Guid.NewGuid();
        var character = Character.CreateDraft(identityId, DateTimeOffset.UtcNow);
        character.SetNameOnce("TestChar", DateTimeOffset.UtcNow);
        character.AllocatePoints(1, 1, 1, 0, DateTimeOffset.UtcNow);
        _characters.GetByIdentityIdAsync(identityId, Arg.Any<CancellationToken>())
            .Returns(character);

        var result = await _handler.HandleAsync(new GetCharacterQuery(identityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("TestChar");
        result.Value.Strength.Should().Be(4);
        result.Value.Agility.Should().Be(4);
        result.Value.Intuition.Should().Be(4);
        result.Value.Vitality.Should().Be(3);
        result.Value.State.Should().Be(OnboardingState.Ready);
    }
}
