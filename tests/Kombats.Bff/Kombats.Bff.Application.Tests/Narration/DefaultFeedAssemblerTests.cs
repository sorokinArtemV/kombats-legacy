using FluentAssertions;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;
using Xunit;

namespace Kombats.Bff.Application.Tests.Narration;

public class DefaultFeedAssemblerTests
{
    private readonly DefaultFeedAssembler _assembler = new();

    private static readonly NarrationTemplate TestTemplate = new()
    {
        Category = "attack.hit",
        Template = "test",
        Tone = FeedEntryTone.Neutral,
        Severity = FeedEntrySeverity.Normal
    };

    [Fact]
    public void EntryKey_FollowsFormat()
    {
        var battleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var entry = _assembler.CreateEntry(battleId, 3, 1, FeedEntryKind.AttackHit, TestTemplate, "text");

        entry.Key.Should().Be("11111111-1111-1111-1111-111111111111:3:1");
    }

    [Fact]
    public void Entry_CarriesBattleIdAndTurnIndex()
    {
        var battleId = Guid.NewGuid();
        var entry = _assembler.CreateEntry(battleId, 5, 0, FeedEntryKind.AttackCrit, TestTemplate, "crit!");

        entry.BattleId.Should().Be(battleId);
        entry.TurnIndex.Should().Be(5);
        entry.Sequence.Should().Be(0);
        entry.Kind.Should().Be(FeedEntryKind.AttackCrit);
        entry.Text.Should().Be("crit!");
    }

    [Fact]
    public void Entry_UsesTemplateSeverityAndTone()
    {
        var template = new NarrationTemplate
        {
            Category = "attack.crit",
            Template = "crit",
            Tone = FeedEntryTone.Aggressive,
            Severity = FeedEntrySeverity.Important
        };

        var entry = _assembler.CreateEntry(Guid.NewGuid(), 1, 0, FeedEntryKind.AttackCrit, template, "text");

        entry.Severity.Should().Be(FeedEntrySeverity.Important);
        entry.Tone.Should().Be(FeedEntryTone.Aggressive);
    }

    [Fact]
    public void Update_ContainsAllEntries()
    {
        var battleId = Guid.NewGuid();
        var entries = new[]
        {
            _assembler.CreateEntry(battleId, 1, 0, FeedEntryKind.AttackHit, TestTemplate, "hit"),
            _assembler.CreateEntry(battleId, 1, 1, FeedEntryKind.AttackDodge, TestTemplate, "dodge")
        };

        var update = _assembler.CreateUpdate(battleId, entries);

        update.BattleId.Should().Be(battleId);
        update.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void SequenceNumbering_IsPreserved()
    {
        var battleId = Guid.NewGuid();
        var entry0 = _assembler.CreateEntry(battleId, 1, 0, FeedEntryKind.AttackHit, TestTemplate, "a");
        var entry1 = _assembler.CreateEntry(battleId, 1, 1, FeedEntryKind.AttackHit, TestTemplate, "b");
        var entry2 = _assembler.CreateEntry(battleId, 1, 2, FeedEntryKind.CommentaryFirstBlood, TestTemplate, "c");

        entry0.Sequence.Should().Be(0);
        entry1.Sequence.Should().Be(1);
        entry2.Sequence.Should().Be(2);
    }
}
