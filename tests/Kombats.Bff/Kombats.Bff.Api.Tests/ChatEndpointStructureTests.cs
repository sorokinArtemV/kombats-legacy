using FluentAssertions;
using Kombats.Bff.Api.Endpoints;
using Kombats.Bff.Api.Endpoints.Chat;
using Kombats.Bff.Api.Endpoints.PlayerCard;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class ChatEndpointStructureTests
{
    [Theory]
    [InlineData(typeof(GetConversationsEndpoint))]
    [InlineData(typeof(GetConversationMessagesEndpoint))]
    [InlineData(typeof(GetDirectMessagesEndpoint))]
    [InlineData(typeof(GetOnlinePlayersEndpoint))]
    [InlineData(typeof(GetPlayerCardEndpoint))]
    public void Batch5Endpoints_ImplementIEndpoint_AndAreSealed(Type t)
    {
        t.Should().BeAssignableTo<IEndpoint>();
        t.IsSealed.Should().BeTrue();
    }
}
