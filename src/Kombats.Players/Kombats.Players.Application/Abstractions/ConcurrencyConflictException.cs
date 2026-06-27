namespace Kombats.Players.Application.Abstractions;

internal sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("A concurrency conflict occurred.", innerException)
    {
    }
}
