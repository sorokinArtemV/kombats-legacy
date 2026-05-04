using Kombats.Players.Domain.Exceptions;
using Kombats.Players.Domain.Progression;

namespace Kombats.Players.Domain.Entities;

public sealed class Character
{
    private Character()
    {
    }

    public Guid Id { get; private set; }
    public Guid IdentityId { get; private set; }
    public string? Name { get; private set; }

    public int Strength { get; private set; }
    public int Agility { get; private set; }
    public int Intuition { get; private set; }
    public int Vitality { get; private set; }

    public int UnspentPoints { get; private set; }
    public int Revision { get; private set; }
    public OnboardingState OnboardingState { get; private set; }

    public string AvatarId { get; private set; } = AvatarCatalog.Default;

    public long TotalXp { get; private set; }
    public int Level { get; private set; }
    public int LevelingVersion { get; private set; }

    public int Wins { get; private set; }
    public int Losses { get; private set; }

    public bool IsReady => OnboardingState == OnboardingState.Ready;

    public DateTimeOffset Created { get; private set; }
    public DateTimeOffset Updated { get; private set; }

    public static Character CreateDraft(Guid identityId, DateTimeOffset occurredAt)
    {
        return new Character
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            Strength = 3,
            Agility = 3,
            Intuition = 3,
            Vitality = 3,
            UnspentPoints = 3,
            TotalXp = 0,
            Level = 0,
            LevelingVersion = 1,
            Wins = 0,
            Losses = 0,
            Revision = 1,
            OnboardingState = OnboardingState.Draft,
            AvatarId = AvatarCatalog.Default,
            Created = occurredAt,
            Updated = occurredAt
        };
    }

    public void ChangeAvatar(string avatarId, DateTimeOffset occurredAt)
    {
        if (!AvatarCatalog.IsValid(avatarId))
        {
            throw new DomainException("InvalidAvatar", "Avatar id is not in the allowed catalog.");
        }

        if (AvatarId == avatarId)
        {
            return;
        }

        AvatarId = avatarId;
        Revision++;
        Updated = occurredAt;
    }

    public void SetNameOnce(string displayName, DateTimeOffset occurredAt)
    {
        if (OnboardingState != OnboardingState.Draft)
        {
            throw new DomainException("InvalidState", "Name can only be set when character is in Draft state.");
        }

        if (Name is not null)
        {
            throw new DomainException("NameAlreadySet", "Name has already been set.");
        }

        var name = displayName.Trim();
        if (name.Length < 3 || name.Length > 16)
        {
            throw new DomainException("InvalidName", "Name must be between 3 and 16 characters.");
        }

        Name = name;
        OnboardingState = OnboardingState.Named;
        Revision++;
        Updated = occurredAt;
    }

    public void AllocatePoints(int str, int agi, int intuition, int vit, DateTimeOffset occurredAt)
    {
        if (OnboardingState != OnboardingState.Named && OnboardingState != OnboardingState.Ready)
        {
            throw new DomainException("InvalidState", "Stats can only be allocated when character is Named or Ready.");
        }

        if (str < 0 || agi < 0 || intuition < 0 || vit < 0)
        {
            throw new DomainException("NegativePoints", "Stat point values cannot be negative.");
        }

        var total = str + agi + intuition + vit;
        if (total == 0)
            throw new DomainException("ZeroPoints", "Must allocate at least one stat point.");

        if (total > UnspentPoints)
            throw new DomainException("NotEnoughPoints", "Insufficient unspent points to allocate.");

        Strength += str;
        Agility += agi;
        Intuition += intuition;
        Vitality += vit;

        UnspentPoints -= total;

        if (OnboardingState == OnboardingState.Named)
        {
            OnboardingState = OnboardingState.Ready;
        }

        Revision++;
        Updated = occurredAt;
    }

    public void AddExperience(long amount, LevelingConfig config, DateTimeOffset occurredAt)
    {
        if (amount <= 0)
            throw new DomainException("InvalidXp", "Experience amount must be greater than zero.");

        var oldLevel = Level;

        checked
        {
            TotalXp += amount;
        }

        Level = LevelingPolicyV1.LevelForTotalXp(TotalXp, config, LevelingVersion);

        var levelsGained = Level - oldLevel;
        if (levelsGained > 0)
        {
            UnspentPoints += levelsGained;
        }

        Revision++;
        Updated = occurredAt;
    }

    public void RecordWin(DateTimeOffset occurredAt)
    {
        Wins++;
        Revision++;
        Updated = occurredAt;
    }

    public void RecordLoss(DateTimeOffset occurredAt)
    {
        Losses++;
        Revision++;
        Updated = occurredAt;
    }
}
