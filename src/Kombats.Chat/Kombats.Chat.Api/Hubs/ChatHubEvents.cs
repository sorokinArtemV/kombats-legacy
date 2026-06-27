namespace Kombats.Chat.Api.Hubs;

/// <summary>
/// Frozen Batch 3 server-to-client SignalR event names. Batch 5 (BFF) consumes these verbatim.
/// </summary>
internal static class ChatHubEvents
{
    public const string GlobalMessageReceived = "GlobalMessageReceived";
    public const string DirectMessageReceived = "DirectMessageReceived";
    public const string PlayerOnline = "PlayerOnline";
    public const string PlayerOffline = "PlayerOffline";
    public const string ChatError = "ChatError";
}
