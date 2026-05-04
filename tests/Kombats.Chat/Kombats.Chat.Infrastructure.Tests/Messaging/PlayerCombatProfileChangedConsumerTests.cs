using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Chat.Application.UseCases.HandlePlayerProfileChanged;
using Kombats.Chat.Infrastructure.Messaging.Consumers;
using Kombats.Players.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Messaging;

public sealed class PlayerCombatProfileChangedConsumerTests
{
    private readonly ICommandHandler<HandlePlayerProfileChangedCommand> _handler
        = Substitute.For<ICommandHandler<HandlePlayerProfileChangedCommand>>();

    private PlayerCombatProfileChangedConsumer CreateConsumer() =>
        new(_handler, NullLogger<PlayerCombatProfileChangedConsumer>.Instance);

    [Fact]
    public async Task IsReadyTrue_DelegatesToHandler_WithReadyCommand()
    {
        _handler.HandleAsync(Arg.Any<HandlePlayerProfileChangedCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var msg = CreateMessage(Guid.NewGuid(), "Alice", isReady: true);
        var ctx = CreateContext(msg);

        await CreateConsumer().Consume(ctx);

        await _handler.Received(1).HandleAsync(
            Arg.Is<HandlePlayerProfileChangedCommand>(c =>
                c.IdentityId == msg.IdentityId && c.Name == "Alice" && c.IsReady),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsReadyFalse_DelegatesToHandler_WithNotReadyCommand()
    {
        _handler.HandleAsync(Arg.Any<HandlePlayerProfileChangedCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var msg = CreateMessage(Guid.NewGuid(), "Bob", isReady: false);

        await CreateConsumer().Consume(CreateContext(msg));

        await _handler.Received(1).HandleAsync(
            Arg.Is<HandlePlayerProfileChangedCommand>(c =>
                c.IdentityId == msg.IdentityId && c.Name == "Bob" && !c.IsReady),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NullName_IsPropagatedToHandler()
    {
        _handler.HandleAsync(Arg.Any<HandlePlayerProfileChangedCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var msg = CreateMessage(Guid.NewGuid(), name: null, isReady: true);

        await CreateConsumer().Consume(CreateContext(msg));

        await _handler.Received(1).HandleAsync(
            Arg.Is<HandlePlayerProfileChangedCommand>(c => c.Name == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameMessageTwice_HandlerInvokedTwice_AndRemainsSafe()
    {
        // Application-level idempotency check: the handler is write-only SetAsync,
        // so replaying the same message yields the same cache state (last-write-wins
        // with identical payload). This covers the application-level safety.
        // MassTransit's inbox provides transport-level dedup (tested in integration
        // by the EF Core outbox/inbox framework itself).
        _handler.HandleAsync(Arg.Any<HandlePlayerProfileChangedCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var msg = CreateMessage(Guid.NewGuid(), "Replay", isReady: true);
        var consumer = CreateConsumer();

        await consumer.Consume(CreateContext(msg));
        await consumer.Consume(CreateContext(msg));

        await _handler.Received(2).HandleAsync(
            Arg.Is<HandlePlayerProfileChangedCommand>(c => c.IdentityId == msg.IdentityId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandlerFailure_Throws_SoMassTransitRetriesOrFaults()
    {
        // Consumer MUST surface handler failure: swallowing it would cause the inbox
        // to mark the message as successfully processed and leave the cache stale until
        // TTL expiry. Throwing lets MassTransit retry/redelivery/fault kick in.
        _handler.HandleAsync(Arg.Any<HandlePlayerProfileChangedCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Failure("test", "intentional")));

        var msg = CreateMessage(Guid.NewGuid(), "Carol", isReady: true);

        Func<Task> act = async () => await CreateConsumer().Consume(CreateContext(msg));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static PlayerCombatProfileChanged CreateMessage(Guid identityId, string? name, bool isReady) => new()
    {
        MessageId = Guid.NewGuid(),
        IdentityId = identityId,
        CharacterId = Guid.NewGuid(),
        Name = name,
        Level = 7,
        Strength = 10,
        Agility = 10,
        Intuition = 10,
        Vitality = 10,
        IsReady = isReady,
        Revision = 3,
        OccurredAt = DateTimeOffset.UtcNow,
        Version = 1,
    };

    private static ConsumeContext<PlayerCombatProfileChanged> CreateContext(PlayerCombatProfileChanged message)
    {
        var ctx = Substitute.For<ConsumeContext<PlayerCombatProfileChanged>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
