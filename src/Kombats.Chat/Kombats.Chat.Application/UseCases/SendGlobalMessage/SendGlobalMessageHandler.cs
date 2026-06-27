using Kombats.Abstractions;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;

namespace Kombats.Chat.Application.UseCases.SendGlobalMessage;

internal sealed class SendGlobalMessageHandler(
    IEligibilityChecker eligibility,
    IUserRestriction restriction,
    IRateLimiter rateLimiter,
    IMessageFilter messageFilter,
    IDisplayNameResolver displayNames,
    IConversationRepository conversations,
    IMessageRepository messages,
    IChatNotifier notifier,
    TimeProvider timeProvider)
    : ICommandHandler<SendGlobalMessageCommand>
{
    public const string RateLimitSurface = "global";

    public async Task<Result> HandleAsync(SendGlobalMessageCommand command, CancellationToken cancellationToken)
    {
        var check = await eligibility.CheckEligibilityAsync(command.SenderIdentityId, cancellationToken);
        if (!check.Eligible)
        {
            return Result.Failure(ChatError.NotEligible());
        }

        if (!await restriction.CanSendAsync(command.SenderIdentityId, cancellationToken))
        {
            return Result.Failure(ChatError.NotEligible());
        }

        var rate = await rateLimiter.CheckAndIncrementAsync(
            command.SenderIdentityId,
            RateLimitSurface,
            cancellationToken);

        if (!rate.Allowed)
        {
            return Result.Failure(ChatError.RateLimited(rate.RetryAfterMs));
        }

        var filter = messageFilter.Filter(command.Content ?? string.Empty);
        if (!filter.Valid)
        {
            return Result.Failure(filter.ErrorCode == ChatErrorCodes.MessageTooLong
                ? ChatError.MessageTooLong()
                : ChatError.MessageEmpty());
        }

        string senderName = check.DisplayName ?? await displayNames.ResolveAsync(command.SenderIdentityId, cancellationToken);

        DateTimeOffset sentAt = timeProvider.GetUtcNow();
        Message message = Message.Create(
            Conversation.GlobalConversationId,
            command.SenderIdentityId,
            senderName,
            filter.SanitizedContent!,
            sentAt);

        await messages.SaveAsync(message, cancellationToken);
        await conversations.UpdateLastMessageAtAsync(Conversation.GlobalConversationId, sentAt, cancellationToken);

        await notifier.BroadcastGlobalMessageAsync(
            new GlobalMessageEvent(
                message.Id,
                new SenderDto(message.SenderIdentityId, message.SenderDisplayName),
                message.Content,
                message.SentAt),
            cancellationToken);

        return Result.Success();
    }
}
