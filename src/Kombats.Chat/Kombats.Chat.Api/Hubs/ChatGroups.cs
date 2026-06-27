namespace Kombats.Chat.Api.Hubs;

internal static class ChatGroups
{
    /// <summary>
    /// SignalR group name for the global chat. v1 broadcasts <c>PlayerOnline</c>/<c>PlayerOffline</c>
    /// and <c>GlobalMessageReceived</c> to this group only.
    /// </summary>
    public const string Global = "global";

    /// <summary>
    /// Per-identity group used for direct-message delivery. A user's connections
    /// (multi-tab) all join <see cref="ForIdentity"/> on connect so DMs reach every tab.
    /// </summary>
    public static string ForIdentity(Guid identityId) => $"identity:{identityId:D}";
}
