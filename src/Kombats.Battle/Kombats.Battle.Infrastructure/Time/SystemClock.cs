using Kombats.Battle.Application.Ports;

namespace Kombats.Battle.Infrastructure.Time;

/// <summary>
/// System clock implementation of IClock.
/// Uses DateTime.UtcNow.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}










