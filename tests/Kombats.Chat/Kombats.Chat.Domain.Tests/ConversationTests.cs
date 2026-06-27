using FluentAssertions;
using Kombats.Chat.Domain.Conversations;
using Xunit;

namespace Kombats.Chat.Domain.Tests;

public sealed class ConversationTests
{
    [Fact]
    public void GlobalConversationId_IsWellKnownConstant()
    {
        Conversation.GlobalConversationId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }

    [Fact]
    public void CreateGlobal_SetsCorrectProperties()
    {
        var conversation = Conversation.CreateGlobal(Conversation.GlobalConversationId);

        conversation.Id.Should().Be(Conversation.GlobalConversationId);
        conversation.Type.Should().Be(ConversationType.Global);
        conversation.ParticipantAIdentityId.Should().BeNull();
        conversation.ParticipantBIdentityId.Should().BeNull();
        conversation.LastMessageAt.Should().BeNull();
    }

    [Fact]
    public void CreateDirect_SortsParticipants_SmallerGuidFirst()
    {
        var smaller = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var larger = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        // Pass larger first — should still sort correctly
        var conversation = Conversation.CreateDirect(larger, smaller);

        conversation.ParticipantAIdentityId.Should().Be(smaller);
        conversation.ParticipantBIdentityId.Should().Be(larger);
        conversation.Type.Should().Be(ConversationType.Direct);
    }

    [Fact]
    public void CreateDirect_AlreadySorted_PreservesOrder()
    {
        var smaller = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var larger = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var conversation = Conversation.CreateDirect(smaller, larger);

        conversation.ParticipantAIdentityId.Should().Be(smaller);
        conversation.ParticipantBIdentityId.Should().Be(larger);
    }

    [Fact]
    public void CreateDirect_SameParticipant_Throws()
    {
        var id = Guid.NewGuid();

        var act = () => Conversation.CreateDirect(id, id);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*distinct*");
    }

    [Fact]
    public void UpdateLastMessageAt_SetsValue()
    {
        var conversation = Conversation.CreateGlobal(Conversation.GlobalConversationId);
        var now = DateTimeOffset.UtcNow;

        conversation.UpdateLastMessageAt(now);

        conversation.LastMessageAt.Should().Be(now);
    }

    [Fact]
    public void UpdateLastMessageAt_DoesNotGoBackwards()
    {
        var conversation = Conversation.CreateGlobal(Conversation.GlobalConversationId);
        var later = DateTimeOffset.UtcNow;
        var earlier = later.AddMinutes(-5);

        conversation.UpdateLastMessageAt(later);
        conversation.UpdateLastMessageAt(earlier);

        conversation.LastMessageAt.Should().Be(later);
    }

    [Fact]
    public void IsParticipant_GlobalConversation_AlwaysTrue()
    {
        var conversation = Conversation.CreateGlobal(Conversation.GlobalConversationId);

        conversation.IsParticipant(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void IsParticipant_DirectConversation_TrueForParticipants()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var conversation = Conversation.CreateDirect(a, b);

        conversation.IsParticipant(a).Should().BeTrue();
        conversation.IsParticipant(b).Should().BeTrue();
    }

    [Fact]
    public void IsParticipant_DirectConversation_FalseForNonParticipant()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var conversation = Conversation.CreateDirect(a, b);

        conversation.IsParticipant(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void SortParticipants_ReturnsSmallerFirst()
    {
        var smaller = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var larger = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var (a, b) = Conversation.SortParticipants(larger, smaller);

        a.Should().Be(smaller);
        b.Should().Be(larger);
    }

    [Fact]
    public void ConversationType_HasCorrectValues()
    {
        ((int)ConversationType.Global).Should().Be(0);
        ((int)ConversationType.Direct).Should().Be(1);
    }
}
