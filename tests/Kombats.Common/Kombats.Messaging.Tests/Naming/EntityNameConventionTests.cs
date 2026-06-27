using FluentAssertions;
using Kombats.Messaging.Naming;
using Xunit;

namespace Kombats.Messaging.Tests.Naming;

public class EntityNameConventionTests
{
    [Fact]
    public void FormatEntityName_applies_combats_prefix_and_kebab_case()
    {
        var convention = new EntityNameConvention(entityNamePrefix: "combats", useKebabCase: true);

        var result = convention.FormatEntityName<TestMessage>();

        result.Should().Be("combats.test-message");
    }

    [Fact]
    public void FormatEntityName_uses_mapped_name_when_registered()
    {
        var map = new Dictionary<Type, string>
        {
            [typeof(TestMessage)] = "combats.custom-name"
        };
        var convention = new EntityNameConvention(map, "combats", true);

        var result = convention.FormatEntityName<TestMessage>();

        result.Should().Be("combats.custom-name");
    }

    [Fact]
    public void FormatEntityName_without_prefix_produces_kebab_only()
    {
        var convention = new EntityNameConvention(entityNamePrefix: "", useKebabCase: true);

        var result = convention.FormatEntityName<TestMessage>();

        result.Should().Be("test-message");
    }

    [Fact]
    public void FormatEntityName_without_kebab_preserves_original_name()
    {
        var convention = new EntityNameConvention(entityNamePrefix: "combats", useKebabCase: false);

        var result = convention.FormatEntityName<TestMessage>();

        result.Should().Be("combats.TestMessage");
    }

    [Fact]
    public void Default_convention_produces_combats_prefix_with_kebab()
    {
        var convention = new EntityNameConvention();

        var result = convention.FormatEntityName<PlayerCombatProfileChanged>();

        result.Should().Be("combats.player-combat-profile-changed");
    }

    [Fact]
    public void Default_convention_produces_correct_name_for_battle_events()
    {
        var convention = new EntityNameConvention();

        convention.FormatEntityName<BattleCompleted>().Should().Be("combats.battle-completed");
        convention.FormatEntityName<BattleCreated>().Should().Be("combats.battle-created");
        convention.FormatEntityName<CreateBattle>().Should().Be("combats.create-battle");
    }

    // Minimal types to simulate contract message types
    private record TestMessage;
    private record PlayerCombatProfileChanged;
    private record BattleCompleted;
    private record BattleCreated;
    private record CreateBattle;
}
