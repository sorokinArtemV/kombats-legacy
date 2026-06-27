namespace Kombats.Matchmaking.Infrastructure;

/// <summary>
/// Singleton service that provides a stable instance ID for the current process.
/// The instance ID is created once on startup and remains constant for the lifetime of the process.
/// </summary>
internal sealed class InstanceIdService
{
    /// <summary>
    /// Gets the stable instance ID for this process.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Initializes a new instance of the InstanceIdService with a stable GUID.
    /// </summary>
    public InstanceIdService()
    {
        InstanceId = Guid.NewGuid().ToString("N");
    }
}

