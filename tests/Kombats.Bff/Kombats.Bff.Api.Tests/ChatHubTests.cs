using System.Reflection;
using FluentAssertions;
using Kombats.Bff.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class ChatHubTests
{
    [Fact]
    public void ChatHub_HasAuthorizeAttribute()
    {
        typeof(ChatHub).GetCustomAttribute<AuthorizeAttribute>()
            .Should().NotBeNull("ChatHub must require authentication for all connections");
    }

    [Fact]
    public void ChatHub_IsSealed()
    {
        typeof(ChatHub).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void ChatHub_HasJoinGlobalChatMethod()
    {
        var method = typeof(ChatHub).GetMethod("JoinGlobalChat");
        method.Should().NotBeNull();
        method!.GetParameters().Should().BeEmpty();
        method.ReturnType.Should().Be(typeof(Task<object?>));
    }

    [Fact]
    public void ChatHub_HasLeaveGlobalChatMethod()
    {
        var method = typeof(ChatHub).GetMethod("LeaveGlobalChat");
        method.Should().NotBeNull();
        method!.GetParameters().Should().BeEmpty();
        method.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void ChatHub_HasSendGlobalMessageMethod()
    {
        var method = typeof(ChatHub).GetMethod("SendGlobalMessage");
        method.Should().NotBeNull();
        var ps = method!.GetParameters();
        ps.Should().HaveCount(1);
        ps[0].ParameterType.Should().Be(typeof(string));
        ps[0].Name.Should().Be("content");
        method.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void ChatHub_HasSendDirectMessageMethod()
    {
        var method = typeof(ChatHub).GetMethod("SendDirectMessage");
        method.Should().NotBeNull();
        var ps = method!.GetParameters();
        ps.Should().HaveCount(2);
        ps[0].ParameterType.Should().Be(typeof(Guid));
        ps[0].Name.Should().Be("recipientPlayerId");
        ps[1].ParameterType.Should().Be(typeof(string));
        ps[1].Name.Should().Be("content");
        method.ReturnType.Should().Be(typeof(Task<object?>));
    }

    [Fact]
    public void ChatHub_OverridesOnConnectedAsync_AndOnDisconnectedAsync()
    {
        var connected = typeof(ChatHub).GetMethod(
            "OnConnectedAsync",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes);
        connected.Should().NotBeNull();
        connected!.DeclaringType.Should().Be(typeof(ChatHub),
            "ChatHub must override OnConnectedAsync to open the downstream relay");

        var disconnected = typeof(ChatHub).GetMethod(
            "OnDisconnectedAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(Exception)]);
        disconnected.Should().NotBeNull();
        disconnected!.DeclaringType.Should().Be(typeof(ChatHub),
            "ChatHub must override OnDisconnectedAsync to dispose the downstream relay");
    }
}
