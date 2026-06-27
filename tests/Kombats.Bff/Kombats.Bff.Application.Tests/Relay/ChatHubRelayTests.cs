using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kombats.Bff.Application.Tests.Relay;

public sealed class ChatHubRelayTests
{
    private static ServicesOptions Options(string chatBaseUrl = "http://localhost:5004") => new()
    {
        Players = new ServiceOptions { BaseUrl = "http://localhost:5001" },
        Matchmaking = new ServiceOptions { BaseUrl = "http://localhost:5002" },
        Battle = new ServiceOptions { BaseUrl = "http://localhost:5003" },
        Chat = new ServiceOptions { BaseUrl = chatBaseUrl }
    };

    private static ChatHubRelay CreateRelay(IFrontendChatSender? sender = null, string chatBaseUrl = "http://localhost:5004") =>
        new(
            Microsoft.Extensions.Options.Options.Create(Options(chatBaseUrl)),
            sender ?? Substitute.For<IFrontendChatSender>(),
            NullLogger<ChatHubRelay>.Instance);

    [Fact]
    public void ChatHubRelay_ImplementsIChatHubRelay()
    {
        CreateRelay().Should().BeAssignableTo<IChatHubRelay>();
    }

    [Fact]
    public void ChatHubRelay_ImplementsIAsyncDisposable()
    {
        CreateRelay().Should().BeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public void ChatHubRelay_RelayedEventNames_MatchFrozenBatch3Contract()
    {
        // The five frozen Batch 3 server-to-client events. Renaming any of these
        // breaks the BFF↔Chat contract — this test must fail in that case.
        ChatHubRelay.RelayedEventNames.Should().BeEquivalentTo(
            "GlobalMessageReceived",
            "DirectMessageReceived",
            "PlayerOnline",
            "PlayerOffline",
            "ChatError");
    }

    [Fact]
    public void ChatHubRelay_DefaultInvocationTimeout_Is15Seconds()
    {
        ChatHubRelay.DefaultInvocationTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ChatHubRelay_ConnectionLostEvent_IsExpectedName()
    {
        ChatHubRelay.ChatConnectionLostEvent.Should().Be("ChatConnectionLost");
    }

    [Fact]
    public async Task DisconnectAsync_NoConnection_DoesNotThrow()
    {
        var relay = CreateRelay();
        Func<Task> act = () => relay.DisconnectAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_NoConnections_DoesNotThrow()
    {
        var relay = CreateRelay();
        Func<Task> act = async () => await relay.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task JoinGlobalChat_WithoutConnect_ThrowsInvalidOperation()
    {
        var relay = CreateRelay();
        Func<Task> act = () => relay.JoinGlobalChatAsync("connection-1");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active chat relay connection*");
    }

    [Fact]
    public async Task SendGlobalMessage_WithoutConnect_ThrowsInvalidOperation()
    {
        var relay = CreateRelay();
        Func<Task> act = () => relay.SendGlobalMessageAsync("connection-1", "hi");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LeaveGlobalChat_WithoutConnect_ThrowsInvalidOperation()
    {
        var relay = CreateRelay();
        Func<Task> act = () => relay.LeaveGlobalChatAsync("connection-1");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendDirectMessage_WithoutConnect_ThrowsInvalidOperation()
    {
        var relay = CreateRelay();
        Func<Task> act = () => relay.SendDirectMessageAsync("connection-1", Guid.NewGuid(), "hi");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ConnectAsync_UnreachableHost_ThrowsAndCleansUp()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender, "http://unreachable-host-9999.invalid:9");

        Func<Task> act = () => relay.ConnectAsync("connection-1", "fake-jwt");
        await act.Should().ThrowAsync<Exception>();

        // Subsequent invokes must fail because the relay tracking was rolled back.
        Func<Task> joinAct = () => relay.JoinGlobalChatAsync("connection-1");
        await joinAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void IChatHubRelay_HasFrozenBatch3HubMethods()
    {
        var relayType = typeof(IChatHubRelay);
        relayType.GetMethod("ConnectAsync").Should().NotBeNull();
        relayType.GetMethod("DisconnectAsync").Should().NotBeNull();
        relayType.GetMethod("JoinGlobalChatAsync").Should().NotBeNull();
        relayType.GetMethod("LeaveGlobalChatAsync").Should().NotBeNull();
        relayType.GetMethod("SendGlobalMessageAsync").Should().NotBeNull();
        relayType.GetMethod("SendDirectMessageAsync").Should().NotBeNull();
    }
}
