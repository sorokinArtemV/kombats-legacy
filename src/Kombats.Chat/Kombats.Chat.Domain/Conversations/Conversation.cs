namespace Kombats.Chat.Domain.Conversations;

public sealed class Conversation
{
    public static readonly Guid GlobalConversationId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Conversation() { }

    public Guid Id { get; private set; }
    public ConversationType Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public Guid? ParticipantAIdentityId { get; private set; }
    public Guid? ParticipantBIdentityId { get; private set; }

    public static Conversation CreateGlobal(Guid wellKnownId)
    {
        return new Conversation
        {
            Id = wellKnownId,
            Type = ConversationType.Global,
            CreatedAt = DateTimeOffset.UtcNow,
            LastMessageAt = null,
            ParticipantAIdentityId = null,
            ParticipantBIdentityId = null,
        };
    }

    public static Conversation CreateDirect(Guid participantA, Guid participantB)
    {
        if (participantA == participantB)
            throw new ArgumentException("Direct conversation requires two distinct participants.");

        // Sorted-pair invariant: smaller GUID always stored as ParticipantA
        var (a, b) = participantA.CompareTo(participantB) < 0
            ? (participantA, participantB)
            : (participantB, participantA);

        return new Conversation
        {
            Id = Guid.CreateVersion7(),
            Type = ConversationType.Direct,
            CreatedAt = DateTimeOffset.UtcNow,
            LastMessageAt = null,
            ParticipantAIdentityId = a,
            ParticipantBIdentityId = b,
        };
    }

    public void UpdateLastMessageAt(DateTimeOffset sentAt)
    {
        if (LastMessageAt is null || sentAt > LastMessageAt)
            LastMessageAt = sentAt;
    }

    public bool IsParticipant(Guid identityId)
    {
        return Type == ConversationType.Global
            || ParticipantAIdentityId == identityId
            || ParticipantBIdentityId == identityId;
    }

    /// <summary>
    /// Returns the sorted participant pair (smaller, larger) for deterministic DM lookup.
    /// </summary>
    public static (Guid A, Guid B) SortParticipants(Guid one, Guid two)
    {
        return one.CompareTo(two) < 0 ? (one, two) : (two, one);
    }
}
