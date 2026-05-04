using FluentAssertions;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Rules;

public class RulesetTests
{
    [Fact]
    public void Create_WithValidParams_Succeeds()
    {
        var ruleset = TestHelpers.DefaultRuleset(seed: 123);

        ruleset.Version.Should().Be(1);
        ruleset.TurnSeconds.Should().Be(30);
        ruleset.NoActionLimit.Should().Be(10);
        ruleset.Seed.Should().Be(123);
        ruleset.Balance.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidVersion_Throws(int version)
    {
        var act = () => Ruleset.Create(version, 30, 10, 42, TestHelpers.DefaultBalance);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidTurnSeconds_Throws(int turnSeconds)
    {
        var act = () => Ruleset.Create(1, turnSeconds, 10, 42, TestHelpers.DefaultBalance);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidNoActionLimit_Throws(int limit)
    {
        var act = () => Ruleset.Create(1, 30, limit, 42, TestHelpers.DefaultBalance);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithNullBalance_Throws()
    {
        var act = () => Ruleset.Create(1, 30, 10, 42, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithZeroSeed_Succeeds()
    {
        var ruleset = Ruleset.Create(1, 30, 10, 0, TestHelpers.DefaultBalance);
        ruleset.Seed.Should().Be(0);
    }

    [Fact]
    public void Create_WithNegativeSeed_Succeeds()
    {
        var ruleset = Ruleset.Create(1, 30, 10, -1, TestHelpers.DefaultBalance);
        ruleset.Seed.Should().Be(-1);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var r1 = TestHelpers.DefaultRuleset(42);
        var r2 = TestHelpers.DefaultRuleset(42);
        r1.Should().Be(r2);
    }

    [Fact]
    public void RecordEquality_DifferentSeed_AreNotEqual()
    {
        var r1 = TestHelpers.DefaultRuleset(42);
        var r2 = TestHelpers.DefaultRuleset(43);
        r1.Should().NotBe(r2);
    }
}
