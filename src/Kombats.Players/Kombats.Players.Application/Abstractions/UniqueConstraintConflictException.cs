namespace Kombats.Players.Application.Abstractions;

internal sealed class UniqueConstraintConflictException : Exception
{
    public UniqueConflictKind ConflictKind { get; }

    public UniqueConstraintConflictException(UniqueConflictKind conflictKind, Exception innerException)
        : base($"Unique constraint conflict: {conflictKind}.", innerException)
    {
        ConflictKind = conflictKind;
    }
}
