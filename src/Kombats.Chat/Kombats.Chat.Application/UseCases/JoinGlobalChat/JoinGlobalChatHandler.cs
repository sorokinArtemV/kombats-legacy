using Kombats.Abstractions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.Repositories;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.GetOnlinePlayers;
using Kombats.Chat.Domain.Conversations;

namespace Kombats.Chat.Application.UseCases.JoinGlobalChat;

/// <summary>
/// Authoritative Chat Layer 2 join: enforces eligibility (<c>OnboardingState == Ready</c>),
/// returns the global conversation id, recent messages (newest first, capped 50),
/// and the first page of online players (capped 100) plus the total online count.
///
/// Does NOT mutate presence — that was established in <c>ConnectUser</c>. The hub adds
/// the connection to the global SignalR group on a successful result.
/// </summary>
internal sealed class JoinGlobalChatHandler(
    IEligibilityChecker eligibility,
    IMessageRepository messages,
    IPresenceStore presence)
    : ICommandHandler<JoinGlobalChatCommand, JoinGlobalChatResponse>
{
    public const int RecentMessagesLimit = 50;
    public const int OnlinePlayersInitialLimit = 100;

    public async Task<Result<JoinGlobalChatResponse>> HandleAsync(
        JoinGlobalChatCommand command,
        CancellationToken cancellationToken)
    {
        var check = await eligibility.CheckEligibilityAsync(command.CallerIdentityId, cancellationToken);
        if (!check.Eligible)
        {
            return Result.Failure<JoinGlobalChatResponse>(ChatError.NotEligible());
        }

        var recent = await messages.GetByConversationAsync(
            Conversation.GlobalConversationId,
            before: null,
            RecentMessagesLimit,
            cancellationToken);

        var recentDtos = recent
            .Select(m => new MessageDto(
                m.Id,
                m.ConversationId,
                new SenderDto(m.SenderIdentityId, m.SenderDisplayName),
                m.Content,
                m.SentAt))
            .ToList();

        var online = await presence.GetOnlinePlayersAsync(
            OnlinePlayersInitialLimit,
            offset: 0,
            cancellationToken);

        var onlineDtos = online
            .Select(p => new OnlinePlayerDto(p.PlayerId, p.DisplayName))
            .ToList();

        long totalOnline = await presence.GetOnlineCountAsync(cancellationToken);

        return Result.Success(new JoinGlobalChatResponse(
            Conversation.GlobalConversationId,
            recentDtos,
            onlineDtos,
            totalOnline));
    }
}
