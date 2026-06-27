using FluentAssertions;
using Kombats.Bff.Application.Narration.Templates;
using Xunit;

namespace Kombats.Bff.Application.Tests.Narration;

public class InMemoryTemplateCatalogTests
{
    private readonly InMemoryTemplateCatalog _catalog = new();

    private static readonly string[] ExpectedCategories =
    [
        "attack.hit", "attack.crit", "attack.dodge", "attack.block", "attack.no_action",
        "battle.start", "battle.end.victory", "battle.end.draw", "battle.end.forfeit",
        "defeat.knockout",
        "commentary.first_blood", "commentary.mutual_miss", "commentary.stalemate",
        "commentary.near_death", "commentary.big_hit", "commentary.knockout", "commentary.draw"
    ];

    [Fact]
    public void AllExpectedCategories_HaveTemplates()
    {
        foreach (var category in ExpectedCategories)
        {
            var templates = _catalog.GetTemplates(category);
            templates.Should().NotBeEmpty($"category '{category}' should have at least one template");
        }
    }

    [Fact]
    public void TotalTemplateCount_IsApproximately50()
    {
        var categories = _catalog.GetCategories();
        var total = categories.Sum(c => _catalog.GetTemplates(c).Count);

        total.Should().BeGreaterThanOrEqualTo(46, "V1 should have approximately 50 templates");
        total.Should().BeLessThanOrEqualTo(55);
    }

    [Theory]
    [InlineData("attack.hit", 5)]
    [InlineData("attack.crit", 4)]
    [InlineData("attack.dodge", 4)]
    [InlineData("attack.block", 4)]
    [InlineData("attack.no_action", 3)]
    [InlineData("battle.start", 3)]
    [InlineData("battle.end.victory", 3)]
    [InlineData("battle.end.draw", 2)]
    [InlineData("battle.end.forfeit", 2)]
    [InlineData("defeat.knockout", 3)]
    [InlineData("commentary.first_blood", 3)]
    [InlineData("commentary.mutual_miss", 2)]
    [InlineData("commentary.stalemate", 2)]
    [InlineData("commentary.near_death", 3)]
    [InlineData("commentary.big_hit", 3)]
    [InlineData("commentary.knockout", 3)]
    [InlineData("commentary.draw", 2)]
    public void Category_HasExpectedCount(string category, int expectedCount)
    {
        var templates = _catalog.GetTemplates(category);
        templates.Should().HaveCount(expectedCount);
    }

    [Fact]
    public void UnknownCategory_ReturnsEmpty()
    {
        var templates = _catalog.GetTemplates("nonexistent.category");
        templates.Should().BeEmpty();
    }

    [Fact]
    public void AllTemplates_HaveNonEmptyText()
    {
        foreach (var category in _catalog.GetCategories())
        {
            foreach (var template in _catalog.GetTemplates(category))
            {
                template.Template.Should().NotBeNullOrWhiteSpace();
                template.Category.Should().Be(category);
            }
        }
    }
}
