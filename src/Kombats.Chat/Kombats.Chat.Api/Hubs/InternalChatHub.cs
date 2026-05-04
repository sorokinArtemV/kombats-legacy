using Kombats.Abstractions;
using Kombats.Abstractions.Auth;
using Kombats.Chat.Application;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.UseCases.ConnectUser;
using Kombats.Chat.Application.UseCases.DisconnectUser;
using Kombats.Chat.Application.UseCases.JoinGlobalChat;
using Kombats.Chat.Application.UseCases.SendDirectMessage;
using Kombats.Chat.Application.UseCases.SendGlobalMessage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Kombats.Chat.Api.Hubs;

/// <summary>
/// Internal Chat SignalR hub at <c>/chathub-internal</c>. Authenticated via JWT.
/// Exposes the frozen Batch 3 contract:
/// <c>JoinGlobalChat</c>, <c>LeaveGlobalChat</c>, <c>SendGlobalMessage</c>, <c>SendDirectMessage</c>.
/// Server-to-client events: <c>GlobalMessageReceived</c>, <c>DirectMessageReceived</c>,
/// <c>PlayerOnline</c>, <c>PlayerOffline</c>, <c>ChatError</c>.
/// </summary>
[Authorize]
internal sealed class InternalChatHub(
    ICommandHandler<ConnectUserCommand> connectHandler,
    ICommandHandler<DisconnectUserCommand> disconnectHandler,
    ICommandHandler<JoinGlobalChatCommand, JoinGlobalChatResponse> joinHandler,
    ICommandHandler<SendGlobalMessageCommand> sendGlobalHandler,
    ICommandHandler<SendDirectMessageCommand, SendDirectMessageResponse> sendDirectHandler,
    HeartbeatScheduler heartbeats,
    ILogger<InternalChatHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        Guid? identityId = Context.User?.GetIdentityId();
        if (identityId is null)
        {
            Context.Abort();
            return;
        }

        // DM delivery group: every connection (multi-tab) joins the per-identity group.
        await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroups.ForIdentity(identityId.Value));

        var result = await connectHandler.HandleAsync(
            new ConnectUserCommand(identityId.Value),
            Context.ConnectionAborted);

        if (result.IsFailure)
        {
            logger.LogWarning("ConnectUser failed for {IdentityId}: {Error}", identityId, result.Error.Code);
        }

        heartbeats.Start(Context.ConnectionId, identityId.Value);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        heartbeats.Stop(Context.ConnectionId);

        Guid? identityId = Context.User?.GetIdentityId();
        if (identityId is not null)
        {
            try
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroups.Global);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroups.ForIdentity(identityId.Value));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Group removal during disconnect failed for {ConnectionId}", Context.ConnectionId);
            }

            var result = await disconnectHandler.HandleAsync(
                new DisconnectUserCommand(identityId.Value),
                CancellationToken.None);

            if (result.IsFailure)
            {
                logger.LogWarning("DisconnectUser failed for {IdentityId}: {Error}", identityId, result.Error.Code);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<JoinGlobalChatResponse?> JoinGlobalChat()
    {
        Guid? identityId = Context.User?.GetIdentityId();
        if (identityId is null)
        {
            await SendErrorAsync(ChatError.NotEligible());
            return null;
        }

        var result = await joinHandler.HandleAsync(
            new JoinGlobalChatCommand(identityId.Value),
            Context.ConnectionAborted);

        if (result.IsFailure)
        {
            await SendErrorAsync(result.Error);
            return null;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroups.Global, Context.ConnectionAborted);

        return result.Value;
    }

    public Task LeaveGlobalChat() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroups.Global, Context.ConnectionAborted);

    public async Task SendGlobalMessage(string content)
    {
        Guid? identityId = Context.User?.GetIdentityId();
        if (identityId is null)
        {
            await SendErrorAsync(ChatError.NotEligible());
            return;
        }

        var result = await sendGlobalHandler.HandleAsync(
            new SendGlobalMessageCommand(identityId.Value, content),
            Context.ConnectionAborted);

        if (result.IsFailure)
        {
            await SendErrorAsync(result.Error);
        }
    }

    public async Task<SendDirectMessageResponse?> SendDirectMessage(Guid recipientPlayerId, string content)
    {
        Guid? identityId = Context.User?.GetIdentityId();
        if (identityId is null)
        {
            await SendErrorAsync(ChatError.NotEligible());
            return null;
        }

        var result = await sendDirectHandler.HandleAsync(
            new SendDirectMessageCommand(identityId.Value, recipientPlayerId, content),
            Context.ConnectionAborted);

        if (result.IsFailure)
        {
            await SendErrorAsync(result.Error);
            return null;
        }

        return result.Value;
    }

    private Task SendErrorAsync(Error error)
    {
        long? retry = (error as ChatError)?.RetryAfterMs;
        var payload = new ChatErrorEvent(error.Code, error.Description, retry);
        return Clients.Caller.SendAsync(ChatHubEvents.ChatError, payload, Context.ConnectionAborted);
    }
}
