using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.JoinGlobalChat;

internal sealed record JoinGlobalChatCommand(Guid CallerIdentityId) : ICommand<JoinGlobalChatResponse>;
