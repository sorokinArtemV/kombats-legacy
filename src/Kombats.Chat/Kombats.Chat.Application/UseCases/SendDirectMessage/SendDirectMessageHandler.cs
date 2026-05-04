using Kombats.Abstractions;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;

namespace Kombats.Chat.Application.UseCases.SendDirectMessage;

internal sealed class SendDirectMessageHandler(
    IEligibilityChecker eligibility,
    IUserRestriction restriction,
    IRateLimiter rateLimiter,
    IMessageFilter messageFilter,
    IDisplayNameResolver displayNames,
    IConversationRepository conversations,
    IMessageRepository messages,
    IChatNotifier notifier,
    TimeProvider timeProvider)
    : ICommandHandler<SendDirectMessageCommand, SendDirectMessageResponse>
{
    public const string RateLimitSurface = "dm";

    public async Task<Result<SendDirectMessageResponse>> HandleAsync(
        SendDirectMessageCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SenderIdentityId == command.RecipientIdentityId)
        {
            return Result.Failure<SendDirectMessageResponse>(ChatError.RecipientNotFound());
        }

        var senderCheck = await eligibility.CheckEligibilityAsync(command.SenderIdentityId, cancellationToken);
        if (!senderCheck.Eligible)
        {
            return Result.Failure<SendDirectMessageResponse>(ChatError.NotEligible());
        }

        if (!await restriction.CanSendAsync(command.SenderIdentityId, cancellationToken))
        {
            return Result.Failure<SendDirectMessageResponse>(ChatError.NotEligible());
        }

        var recipientCheck = await eligibility.CheckEligibilityAsync(command.RecipientIdentityId, cancellationToken);
        if (!recipientCheck.Eligible)
        {
            return Result.Failure<SendDirectMessageResponse>(ChatError.RecipientNotFound());
        }

        var rate = await rateLimiter.CheckAndIncrementAsync(
            command.SenderIdentityId,
            RateLimitSurface,
            cancellationToken);

        if (!rate.Allowed)
        {
            return Result.Failure<SendDirectMessageResponse>(ChatError.RateLimited(rate.RetryAfterMs));
        }

        var filter = messageFilter.Filter(command.Content ?? string.Empty);
        if (!filter.Valid)
        {
            return Result.Failure<SendDirectMessageResponse>(filter.ErrorCode == ChatErrorCodes.MessageTooLong
                ? ChatError.MessageTooLong()
                : ChatError.MessageEmpty());
        }

        string senderName = senderCheck.DisplayName ?? await displayNames.ResolveAsync(command.SenderIdentityId, cancellationToken);

        Conversation? conversation = await conversations.GetOrCreateDirectAsync(
            command.SenderIdentityId,
            command.RecipientIdentityId,
            cancellationToken);

        if (conversation is null)
        {
            return Result.Failure<SendDirectMessageResponse>(
                ChatError.ServiceUnavailable("Could not resolve direct conversation."));
        }

        DateTimeOffset sentAt = timeProvider.GetUtcNow();
        Message message = Message.Create(
            conversation.Id,
            command.SenderIdentityId,
            senderName,
            filter.SanitizedContent!,
            sentAt);

        await messages.SaveAsync(message, cancellationToken);
        await conversations.UpdateLastMessageAtAsync(conversation.Id, sentAt, cancellationToken);

        await notifier.SendDirectMessageAsync(
            command.RecipientIdentityId,
            new DirectMessageEvent(
                message.Id,
                conversation.Id,
                new SenderDto(message.SenderIdentityId, message.SenderDisplayName),
                message.Content,
                message.SentAt),
            cancellationToken);

        return Result.Success(new SendDirectMessageResponse(conversation.Id, message.Id, message.SentAt));
    }
}
