using FluentAssertions;
using Kombats.Matchmaking.Api.Endpoints.Queue;
using Xunit;

namespace Kombats.Matchmaking.Api.Tests;

/// <summary>
/// Verifies Matchmaking API response contracts and DTO structures.
/// Tests that DTOs used in endpoint responses have the expected shape
/// and can be constructed correctly for all response scenarios.
/// </summary>
public sealed class ResponseContractTests
{
    [Fact]
    public void QueueStatusDto_Searching_HasCorrectShape()
    {
        var dto = new QueueStatusDto("Searching");
        dto.Status.Should().Be("Searching");
        dto.MatchId.Should().BeNull();
        dto.BattleId.Should().BeNull();
        dto.MatchState.Should().BeNull();
    }

    [Fact]
    public void QueueStatusDto_Matched_HasAllFields()
    {
        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var dto = new QueueStatusDto("Matched", matchId, battleId, "BattleCreated");
        dto.Status.Should().Be("Matched");
        dto.MatchId.Should().Be(matchId);
        dto.BattleId.Should().Be(battleId);
        dto.MatchState.Should().Be("BattleCreated");
    }

    [Fact]
    public void JoinQueueRequest_AcceptsNullVariant()
    {
        var request = new JoinQueueRequest(null);
        request.Variant.Should().BeNull();
    }

    [Fact]
    public void JoinQueueRequest_AcceptsVariant()
    {
        var request = new JoinQueueRequest("ranked");
        request.Variant.Should().Be("ranked");
    }

    [Fact]
    public void LeaveQueueRequest_AcceptsNullVariant()
    {
        var request = new LeaveQueueRequest(null);
        request.Variant.Should().BeNull();
    }

    [Fact]
    public void LeaveQueueRequest_AcceptsVariant()
    {
        var request = new LeaveQueueRequest("default");
        request.Variant.Should().Be("default");
    }
}
