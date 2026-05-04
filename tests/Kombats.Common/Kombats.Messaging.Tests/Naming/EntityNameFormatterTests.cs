using FluentAssertions;
using Kombats.Messaging.Naming;
using Xunit;

namespace Kombats.Messaging.Tests.Naming;

public class EntityNameFormatterTests
{
    [Theory]
    [InlineData("PlayerCombatProfileChanged", "player-combat-profile-changed")]
    [InlineData("BattleCompleted", "battle-completed")]
    [InlineData("CreateBattle", "create-battle")]
    [InlineData("BattleCreated", "battle-created")]
    [InlineData("MatchCreated", "match-created")]
    [InlineData("MatchCompleted", "match-completed")]
    public void ToKebabCase_converts_PascalCase_to_kebab(string input, string expected)
    {
        EntityNameFormatter.ToKebabCase(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void ToKebabCase_handles_empty_and_whitespace(string? input, string expected)
    {
        EntityNameFormatter.ToKebabCase(input!).Should().Be(expected);
    }

    [Theory]
    [InlineData("already-kebab", "already-kebab")]
    [InlineData("lowercase", "lowercase")]
    public void ToKebabCase_preserves_already_lowercase(string input, string expected)
    {
        EntityNameFormatter.ToKebabCase(input).Should().Be(expected);
    }

    [Fact]
    public void FormatQueueName_produces_service_dot_endpoint()
    {
        var result = EntityNameFormatter.FormatQueueName("matchmaking", "BattleCompleted");

        result.Should().Be("matchmaking.battle-completed");
    }

    [Fact]
    public void FormatEntityName_converts_to_kebab()
    {
        var result = EntityNameFormatter.FormatEntityName("PlayerCombatProfileChanged");

        result.Should().Be("player-combat-profile-changed");
    }
}
