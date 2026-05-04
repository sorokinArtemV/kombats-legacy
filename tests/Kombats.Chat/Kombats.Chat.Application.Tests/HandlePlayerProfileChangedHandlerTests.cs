using FluentAssertions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.UseCases.HandlePlayerProfileChanged;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Application.Tests;

public sealed class HandlePlayerProfileChangedHandlerTests
{
    private readonly IPlayerInfoCache _cache = Substitute.For<IPlayerInfoCache>();
    private readonly HandlePlayerProfileChangedHandler _handler;

    public HandlePlayerProfileChangedHandlerTests()
    {
        _handler = new HandlePlayerProfileChangedHandler(_cache);
    }

    [Fact]
    public async Task IsReady_True_StoresReady()
    {
        var id = Guid.NewGuid();

        var result = await _handler.HandleAsync(
            new HandlePlayerProfileChangedCommand(id, "Alice", IsReady: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _cache.Received(1).SetAsync(
            id,
            Arg.Is<CachedPlayerInfo>(i => i.Name == "Alice" && i.OnboardingState == "Ready" && i.IsEligible),
            Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsReady_False_StoresNotReady_AndNotEligible()
    {
        var id = Guid.NewGuid();

        var result = await _handler.HandleAsync(
            new HandlePlayerProfileChangedCommand(id, "Bob", IsReady: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _cache.Received(1).SetAsync(
            id,
            Arg.Is<CachedPlayerInfo>(i => i.Name == "Bob" && i.OnboardingState == "NotReady" && !i.IsEligible),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NullOrBlankName_IsIgnored_DoesNotTouchCache(string? name)
    {
        var id = Guid.NewGuid();

        var result = await _handler.HandleAsync(
            new HandlePlayerProfileChangedCommand(id, name, IsReady: true),
            CancellationToken.None);

        // Pre-naming EnsureCharacter event: ignored to avoid destructive overwrite of
        // a valid later state under retry/redelivery reordering.
        result.IsSuccess.Should().BeTrue();
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<Guid>(),
            Arg.Any<CachedPlayerInfo>(),
            Arg.Any<CancellationToken>());
    }
}
