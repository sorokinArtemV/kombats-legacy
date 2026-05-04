using FluentAssertions;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Domain.Exceptions;
using Kombats.Players.Domain.Progression;
using Xunit;

namespace Kombats.Players.Domain.Tests.Entities;

public sealed class CharacterCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDraft_SetsCorrectInitialState()
    {
        var identityId = Guid.NewGuid();

        var character = Character.CreateDraft(identityId, Now);

        character.Id.Should().NotBeEmpty();
        character.IdentityId.Should().Be(identityId);
        character.Name.Should().BeNull();
        character.Strength.Should().Be(3);
        character.Agility.Should().Be(3);
        character.Intuition.Should().Be(3);
        character.Vitality.Should().Be(3);
        character.UnspentPoints.Should().Be(3);
        character.TotalXp.Should().Be(0);
        character.Level.Should().Be(0);
        character.LevelingVersion.Should().Be(1);
        character.Wins.Should().Be(0);
        character.Losses.Should().Be(0);
        character.Revision.Should().Be(1);
        character.OnboardingState.Should().Be(OnboardingState.Draft);
        character.IsReady.Should().BeFalse();
        character.AvatarId.Should().Be(AvatarCatalog.Default);
        character.Created.Should().Be(Now);
        character.Updated.Should().Be(Now);
    }

    [Fact]
    public void CreateDraft_GeneratesUniqueIds()
    {
        var c1 = Character.CreateDraft(Guid.NewGuid(), Now);
        var c2 = Character.CreateDraft(Guid.NewGuid(), Now);

        c1.Id.Should().NotBe(c2.Id);
    }
}

public sealed class CharacterSetNameTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private static Character CreateDraft() => Character.CreateDraft(Guid.NewGuid(), Now);

    [Fact]
    public void SetNameOnce_ValidName_SetsNameAndTransitionsToNamed()
    {
        var character = CreateDraft();

        character.SetNameOnce("Hero", Later);

        character.Name.Should().Be("Hero");
        character.OnboardingState.Should().Be(OnboardingState.Named);
        character.IsReady.Should().BeFalse();
        character.Revision.Should().Be(2);
        character.Updated.Should().Be(Later);
    }

    [Fact]
    public void SetNameOnce_TrimsWhitespace()
    {
        var character = CreateDraft();

        character.SetNameOnce("  Hero  ", Later);

        character.Name.Should().Be("Hero");
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("A")]
    [InlineData("")]
    public void SetNameOnce_TooShort_Throws(string name)
    {
        var character = CreateDraft();

        var act = () => character.SetNameOnce(name, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidName");
    }

    [Fact]
    public void SetNameOnce_TooLong_Throws()
    {
        var character = CreateDraft();
        var longName = new string('A', 17);

        var act = () => character.SetNameOnce(longName, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidName");
    }

    [Fact]
    public void SetNameOnce_ExactMinLength_Succeeds()
    {
        var character = CreateDraft();

        character.SetNameOnce("ABC", Later);

        character.Name.Should().Be("ABC");
    }

    [Fact]
    public void SetNameOnce_ExactMaxLength_Succeeds()
    {
        var character = CreateDraft();

        character.SetNameOnce(new string('A', 16), Later);

        character.Name.Should().HaveLength(16);
    }

    [Fact]
    public void SetNameOnce_WhitespaceTrimsToShort_Throws()
    {
        var character = CreateDraft();

        var act = () => character.SetNameOnce("  AB  ", Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidName");
    }

    [Fact]
    public void SetNameOnce_CalledTwice_Throws()
    {
        var character = CreateDraft();
        character.SetNameOnce("Hero", Later);

        var act = () => character.SetNameOnce("Other", Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidState");
    }

    [Fact]
    public void SetNameOnce_WhenReady_Throws()
    {
        var character = CreateDraft();
        character.SetNameOnce("Hero", Later);
        character.AllocatePoints(1, 0, 0, 0, Later);

        var act = () => character.SetNameOnce("Other", Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidState");
    }
}

public sealed class CharacterAllocatePointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private static Character CreateNamed()
    {
        var c = Character.CreateDraft(Guid.NewGuid(), Now);
        c.SetNameOnce("Hero", Now);
        return c;
    }

    [Fact]
    public void AllocatePoints_ValidAllocation_DistributesAndTransitionsToReady()
    {
        var character = CreateNamed();

        character.AllocatePoints(1, 1, 1, 0, Later);

        character.Strength.Should().Be(4);
        character.Agility.Should().Be(4);
        character.Intuition.Should().Be(4);
        character.Vitality.Should().Be(3);
        character.UnspentPoints.Should().Be(0);
        character.OnboardingState.Should().Be(OnboardingState.Ready);
        character.IsReady.Should().BeTrue();
        character.Revision.Should().Be(3);
        character.Updated.Should().Be(Later);
    }

    [Fact]
    public void AllocatePoints_PartialAllocation_KeepsRemainingUnspent()
    {
        var character = CreateNamed();

        character.AllocatePoints(1, 0, 0, 0, Later);

        character.UnspentPoints.Should().Be(2);
        character.Strength.Should().Be(4);
        character.OnboardingState.Should().Be(OnboardingState.Ready);
    }

    [Fact]
    public void AllocatePoints_WhenReady_AllowsSubsequentAllocations()
    {
        var character = CreateNamed();
        character.AllocatePoints(1, 0, 0, 0, Later);

        character.AllocatePoints(1, 0, 0, 0, Later);

        character.UnspentPoints.Should().Be(1);
        character.Strength.Should().Be(5);
        character.OnboardingState.Should().Be(OnboardingState.Ready);
    }

    [Fact]
    public void AllocatePoints_InDraftState_Throws()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);

        var act = () => character.AllocatePoints(1, 0, 0, 0, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidState");
    }

    [Fact]
    public void AllocatePoints_NegativeValues_Throws()
    {
        var character = CreateNamed();

        var act = () => character.AllocatePoints(-1, 0, 0, 0, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "NegativePoints");
    }

    [Fact]
    public void AllocatePoints_ExceedsUnspent_Throws()
    {
        var character = CreateNamed();

        var act = () => character.AllocatePoints(2, 2, 0, 0, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "NotEnoughPoints");
    }

    [Fact]
    public void AllocatePoints_ZeroTotal_Throws()
    {
        var character = CreateNamed();

        var act = () => character.AllocatePoints(0, 0, 0, 0, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "ZeroPoints");
    }

    [Fact]
    public void AllocatePoints_AllToOneStat_Succeeds()
    {
        var character = CreateNamed();

        character.AllocatePoints(3, 0, 0, 0, Later);

        character.Strength.Should().Be(6);
        character.UnspentPoints.Should().Be(0);
    }
}

public sealed class CharacterExperienceTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);
    private static readonly LevelingConfig Config = new(50);

    private static Character CreateReady()
    {
        var c = Character.CreateDraft(Guid.NewGuid(), Now);
        c.SetNameOnce("Hero", Now);
        c.AllocatePoints(1, 1, 1, 0, Now);
        return c;
    }

    [Fact]
    public void AddExperience_ValidAmount_AccumulatesXp()
    {
        var character = CreateReady();

        character.AddExperience(50, Config, Later);

        character.TotalXp.Should().Be(50);
        character.Level.Should().Be(0);
        character.Updated.Should().Be(Later);
    }

    [Fact]
    public void AddExperience_ReachesLevel1Threshold_LevelsUp()
    {
        var character = CreateReady();

        // Level 1 threshold = 50 * 1 * 2 = 100
        character.AddExperience(100, Config, Later);

        character.TotalXp.Should().Be(100);
        character.Level.Should().Be(1);
        character.UnspentPoints.Should().Be(1); // started with 0 unspent after full allocation, gained 1
    }

    [Fact]
    public void AddExperience_MultipleLevelsAtOnce_AwardsAllPoints()
    {
        var character = CreateReady();

        // Level 1 = 100, Level 2 = 300
        character.AddExperience(300, Config, Later);

        character.Level.Should().Be(2);
        character.UnspentPoints.Should().Be(2);
    }

    [Fact]
    public void AddExperience_JustBelowThreshold_DoesNotLevelUp()
    {
        var character = CreateReady();

        character.AddExperience(99, Config, Later);

        character.Level.Should().Be(0);
        character.UnspentPoints.Should().Be(0);
    }

    [Fact]
    public void AddExperience_IncrementalGains_AccumulateCorrectly()
    {
        var character = CreateReady();

        character.AddExperience(50, Config, Later);
        character.AddExperience(50, Config, Later);

        character.TotalXp.Should().Be(100);
        character.Level.Should().Be(1);
    }

    [Fact]
    public void AddExperience_ZeroAmount_Throws()
    {
        var character = CreateReady();

        var act = () => character.AddExperience(0, Config, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidXp");
    }

    [Fact]
    public void AddExperience_NegativeAmount_Throws()
    {
        var character = CreateReady();

        var act = () => character.AddExperience(-1, Config, Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidXp");
    }

    [Fact]
    public void AddExperience_IncrementsRevision()
    {
        var character = CreateReady();
        var revisionBefore = character.Revision;

        character.AddExperience(10, Config, Later);

        character.Revision.Should().Be(revisionBefore + 1);
    }
}

public sealed class CharacterCombatRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private static Character CreateReady()
    {
        var c = Character.CreateDraft(Guid.NewGuid(), Now);
        c.SetNameOnce("Hero", Now);
        c.AllocatePoints(1, 1, 1, 0, Now);
        return c;
    }

    [Fact]
    public void RecordWin_IncrementsWins()
    {
        var character = CreateReady();

        character.RecordWin(Later);

        character.Wins.Should().Be(1);
        character.Losses.Should().Be(0);
        character.Updated.Should().Be(Later);
    }

    [Fact]
    public void RecordLoss_IncrementsLosses()
    {
        var character = CreateReady();

        character.RecordLoss(Later);

        character.Losses.Should().Be(1);
        character.Wins.Should().Be(0);
        character.Updated.Should().Be(Later);
    }

    [Fact]
    public void RecordWin_MultipleTimes_AccumulatesCorrectly()
    {
        var character = CreateReady();

        character.RecordWin(Later);
        character.RecordWin(Later);
        character.RecordWin(Later);

        character.Wins.Should().Be(3);
    }

    [Fact]
    public void RecordLoss_MultipleTimes_AccumulatesCorrectly()
    {
        var character = CreateReady();

        character.RecordLoss(Later);
        character.RecordLoss(Later);

        character.Losses.Should().Be(2);
    }

    [Fact]
    public void RecordWin_IncrementsRevision()
    {
        var character = CreateReady();
        var revisionBefore = character.Revision;

        character.RecordWin(Later);

        character.Revision.Should().Be(revisionBefore + 1);
    }

    [Fact]
    public void RecordLoss_IncrementsRevision()
    {
        var character = CreateReady();
        var revisionBefore = character.Revision;

        character.RecordLoss(Later);

        character.Revision.Should().Be(revisionBefore + 1);
    }
}

public sealed class CharacterIsReadyTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsReady_Draft_ReturnsFalse()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);

        character.IsReady.Should().BeFalse();
    }

    [Fact]
    public void IsReady_Named_ReturnsFalse()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);
        character.SetNameOnce("Hero", Now);

        character.IsReady.Should().BeFalse();
    }

    [Fact]
    public void IsReady_Ready_ReturnsTrue()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);
        character.SetNameOnce("Hero", Now);
        character.AllocatePoints(1, 0, 0, 0, Now);

        character.IsReady.Should().BeTrue();
    }
}

public sealed class CharacterChangeAvatarTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private static Character CreateDraft() => Character.CreateDraft(Guid.NewGuid(), Now);

    [Fact]
    public void ChangeAvatar_ValidId_UpdatesAvatarAndBumpsRevision()
    {
        var character = CreateDraft();
        var revisionBefore = character.Revision;
        var target = AvatarCatalog.AllowedIds.First(id => id != AvatarCatalog.Default);

        character.ChangeAvatar(target, Later);

        character.AvatarId.Should().Be(target);
        character.Revision.Should().Be(revisionBefore + 1);
        character.Updated.Should().Be(Later);
    }

    [Fact]
    public void ChangeAvatar_SameValue_IsNoOp()
    {
        var character = CreateDraft();
        var revisionBefore = character.Revision;
        var updatedBefore = character.Updated;

        character.ChangeAvatar(character.AvatarId, Later);

        character.Revision.Should().Be(revisionBefore);
        character.Updated.Should().Be(updatedBefore);
    }

    [Fact]
    public void ChangeAvatar_InvalidId_Throws()
    {
        var character = CreateDraft();

        var act = () => character.ChangeAvatar("not-a-real-avatar", Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidAvatar");
    }

    [Fact]
    public void ChangeAvatar_EmptyId_Throws()
    {
        var character = CreateDraft();

        var act = () => character.ChangeAvatar("", Later);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidAvatar");
    }

    [Fact]
    public void ChangeAvatar_WorksInAnyOnboardingState()
    {
        var character = CreateDraft();
        character.SetNameOnce("Hero", Now);
        character.AllocatePoints(1, 0, 0, 0, Now);
        var target = AvatarCatalog.AllowedIds.First(id => id != AvatarCatalog.Default);

        character.ChangeAvatar(target, Later);

        character.OnboardingState.Should().Be(OnboardingState.Ready);
        character.AvatarId.Should().Be(target);
    }
}

public sealed class CharacterOnboardingTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FullOnboardingFlow_DraftToNamedToReady()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);
        character.OnboardingState.Should().Be(OnboardingState.Draft);

        character.SetNameOnce("Hero", Now);
        character.OnboardingState.Should().Be(OnboardingState.Named);

        character.AllocatePoints(1, 1, 1, 0, Now);
        character.OnboardingState.Should().Be(OnboardingState.Ready);
    }

    [Fact]
    public void AllocatePoints_InDraft_Throws()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);

        var act = () => character.AllocatePoints(1, 0, 0, 0, Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidState");
    }

    [Fact]
    public void SetName_WhenNamed_Throws()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);
        character.SetNameOnce("Hero", Now);

        var act = () => character.SetNameOnce("Other", Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidState");
    }

    [Fact]
    public void SetName_WhenReady_Throws()
    {
        var character = Character.CreateDraft(Guid.NewGuid(), Now);
        character.SetNameOnce("Hero", Now);
        character.AllocatePoints(1, 0, 0, 0, Now);

        var act = () => character.SetNameOnce("Other", Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "InvalidState");
    }
}
