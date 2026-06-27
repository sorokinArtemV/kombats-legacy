using FluentAssertions;
using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;
using Xunit;

namespace Kombats.Bff.Application.Tests.Narration;

public class DeterministicTemplateSelectorTests
{
    private readonly DeterministicTemplateSelector _selector = new();

    private static readonly NarrationTemplate[] TestTemplates =
    [
        new() { Category = "test", Template = "Template A", Tone = FeedEntryTone.Neutral, Severity = FeedEntrySeverity.Normal },
        new() { Category = "test", Template = "Template B", Tone = FeedEntryTone.Neutral, Severity = FeedEntrySeverity.Normal },
        new() { Category = "test", Template = "Template C", Tone = FeedEntryTone.Neutral, Severity = FeedEntrySeverity.Normal },
        new() { Category = "test", Template = "Template D", Tone = FeedEntryTone.Neutral, Severity = FeedEntrySeverity.Normal },
        new() { Category = "test", Template = "Template E", Tone = FeedEntryTone.Neutral, Severity = FeedEntrySeverity.Normal }
    ];

    [Fact]
    public void SameSeed_SameTemplate()
    {
        var battleId = Guid.NewGuid();
        var result1 = _selector.Select(TestTemplates, battleId, 1, 0);
        var result2 = _selector.Select(TestTemplates, battleId, 1, 0);

        result1.Should().Be(result2);
    }

    [Fact]
    public void DifferentBattleId_MayVary()
    {
        var results = Enumerable.Range(0, 20)
            .Select(_ => _selector.Select(TestTemplates, Guid.NewGuid(), 1, 0))
            .Select(t => t.Template)
            .Distinct()
            .Count();

        // With 20 random battle IDs and 5 templates, we expect at least 2 different selections
        results.Should().BeGreaterThan(1);
    }

    [Fact]
    public void DifferentTurnIndex_MayVary()
    {
        var battleId = Guid.NewGuid();
        var results = Enumerable.Range(1, 20)
            .Select(turn => _selector.Select(TestTemplates, battleId, turn, 0))
            .Select(t => t.Template)
            .Distinct()
            .Count();

        results.Should().BeGreaterThan(1);
    }

    [Fact]
    public void DifferentSequence_MayVary()
    {
        var battleId = Guid.NewGuid();
        var results = Enumerable.Range(0, 20)
            .Select(seq => _selector.Select(TestTemplates, battleId, 1, seq))
            .Select(t => t.Template)
            .Distinct()
            .Count();

        results.Should().BeGreaterThan(1);
    }

    [Fact]
    public void SingleTemplate_AlwaysReturnsThat()
    {
        var single = new[] { TestTemplates[0] };
        var result = _selector.Select(single, Guid.NewGuid(), 1, 0);
        result.Should().Be(TestTemplates[0]);
    }

    [Fact]
    public void EmptyTemplates_Throws()
    {
        var act = () => _selector.Select([], Guid.NewGuid(), 1, 0);
        act.Should().Throw<InvalidOperationException>();
    }
}
