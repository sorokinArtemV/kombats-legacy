using FluentAssertions;
using Kombats.Players.Domain.Exceptions;
using Kombats.Players.Domain.Progression;
using Xunit;

namespace Kombats.Players.Domain.Tests.Progression;

public sealed class LevelingConfigTests
{
    [Fact]
    public void Constructor_PositiveBaseFactor_Succeeds()
    {
        var config = new LevelingConfig(50);

        config.BaseFactor.Should().Be(50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_NonPositiveBaseFactor_Throws(long baseFactor)
    {
        var act = () => new LevelingConfig(baseFactor);

        act.Should().Throw<ArgumentException>();
    }
}

public sealed class LevelingPolicyV1Tests
{
    private static readonly LevelingConfig Config = new(50);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]   // 50 * 1 * 2 = 100
    [InlineData(101, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 2)]   // 50 * 2 * 3 = 300
    [InlineData(599, 2)]
    [InlineData(600, 3)]   // 50 * 3 * 4 = 600
    [InlineData(999, 3)]
    [InlineData(1000, 4)]  // 50 * 4 * 5 = 1000
    public void LevelForTotalXp_ReturnsCorrectLevel(long totalXp, int expectedLevel)
    {
        // Use Character.AddExperience to test indirectly since LevelingPolicyV1 is internal
        var character = CreateReadyCharacter();
        if (totalXp > 0)
        {
            character.AddExperience(totalXp, Config, DateTimeOffset.UtcNow);
        }

        character.Level.Should().Be(expectedLevel);
    }

    [Fact]
    public void LevelUp_AwardsOneUnspentPointPerLevel()
    {
        var character = CreateReadyCharacter();
        // Allocate all initial points first
        character.UnspentPoints.Should().Be(0);

        // Level to 3: threshold = 50 * 3 * 4 = 600
        character.AddExperience(600, Config, DateTimeOffset.UtcNow);

        character.Level.Should().Be(3);
        character.UnspentPoints.Should().Be(3);
    }

    [Fact]
    public void IncrementalXp_LevelsUpCorrectly()
    {
        var character = CreateReadyCharacter();

        // Add XP in small increments
        character.AddExperience(50, Config, DateTimeOffset.UtcNow);
        character.Level.Should().Be(0);

        character.AddExperience(50, Config, DateTimeOffset.UtcNow);
        character.Level.Should().Be(1);
        character.UnspentPoints.Should().Be(1);

        character.AddExperience(200, Config, DateTimeOffset.UtcNow);
        character.Level.Should().Be(2);
        character.UnspentPoints.Should().Be(2);
    }

    [Fact]
    public void Level4_Threshold_IsCorrect()
    {
        var character = CreateReadyCharacter();

        // Level 4 threshold = 50 * 4 * 5 = 1000
        character.AddExperience(999, Config, DateTimeOffset.UtcNow);
        character.Level.Should().Be(3);

        character.AddExperience(1, Config, DateTimeOffset.UtcNow);
        character.Level.Should().Be(4);
    }

    private static Domain.Entities.Character CreateReadyCharacter()
    {
        var now = DateTimeOffset.UtcNow;
        var c = Domain.Entities.Character.CreateDraft(Guid.NewGuid(), now);
        c.SetNameOnce("Hero", now);
        c.AllocatePoints(3, 0, 0, 0, now);
        return c;
    }
}
