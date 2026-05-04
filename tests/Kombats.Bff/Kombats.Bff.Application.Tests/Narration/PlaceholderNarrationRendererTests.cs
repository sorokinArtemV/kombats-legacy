using FluentAssertions;
using Kombats.Bff.Application.Narration;
using Xunit;

namespace Kombats.Bff.Application.Tests.Narration;

public class PlaceholderNarrationRendererTests
{
    private readonly PlaceholderNarrationRenderer _renderer = new();

    [Fact]
    public void Renders_AllPlaceholders()
    {
        var template = "{attackerName} hits {defenderName} for {damage} damage!";
        var placeholders = new Dictionary<string, string>
        {
            ["attackerName"] = "Alice",
            ["defenderName"] = "Bob",
            ["damage"] = "42"
        };

        var result = _renderer.Render(template, placeholders);
        result.Should().Be("Alice hits Bob for 42 damage!");
    }

    [Fact]
    public void MissingPlaceholder_LeftAsIs()
    {
        var template = "{attackerName} hits {unknownField}!";
        var placeholders = new Dictionary<string, string>
        {
            ["attackerName"] = "Alice"
        };

        var result = _renderer.Render(template, placeholders);
        result.Should().Be("Alice hits {unknownField}!");
    }

    [Fact]
    public void NoPlaceholders_ReturnsTemplateAsIs()
    {
        var template = "The battle begins!";
        var result = _renderer.Render(template, new Dictionary<string, string>());
        result.Should().Be("The battle begins!");
    }

    [Fact]
    public void MultipleSamePlaceholder_AllReplaced()
    {
        var template = "{attackerName} vs {attackerName}!";
        var placeholders = new Dictionary<string, string>
        {
            ["attackerName"] = "Alice"
        };

        var result = _renderer.Render(template, placeholders);
        result.Should().Be("Alice vs Alice!");
    }

    [Fact]
    public void CaseInsensitive_Lookup()
    {
        var template = "{AttackerName} strikes!";
        var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["attackername"] = "Alice"
        };

        var result = _renderer.Render(template, placeholders);
        result.Should().Be("Alice strikes!");
    }
}
