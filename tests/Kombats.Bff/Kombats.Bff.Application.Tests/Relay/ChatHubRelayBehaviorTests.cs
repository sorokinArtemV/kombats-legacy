using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kombats.Bff.Application.Tests.Relay;

/// <summary>
/// Behavioural tests for <see cref="ChatHubRelay"/> that exercise the relay
/// against a real, in-process SignalR hub (Kestrel on a random loopback port).
/// Covers: command forwarding, response relay, server-pushed event relay,
/// downstream drop → ChatConnectionLost, hung downstream → invocation timeout.
/// </summary>
public sealed class ChatHubRelayBehaviorTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _baseUrl = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<TestHubState>();

        _app = builder.Build();
        _app.Urls.Clear();
        _app.Urls.Add("http://127.0.0.1:0");
        _app.MapHub<TestChatHub>("/chathub-internal");

        await _app.StartAsync();
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        _baseUrl = addresses!.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private ChatHubRelay CreateRelay(IFrontendChatSender sender, TimeSpan? invocationTimeout = null)
    {
        var services = new ServicesOptions
        {
            Players = new ServiceOptions { BaseUrl = "http://localhost:5001" },
            Matchmaking = new ServiceOptions { BaseUrl = "http://localhost:5002" },
            Battle = new ServiceOptions { BaseUrl = "http://localhost:5003" },
            Chat = new ServiceOptions { BaseUrl = _baseUrl }
        };
        return new ChatHubRelay(
            Options.Create(services),
            sender,
            NullLogger<ChatHubRelay>.Instance,
            invocationTimeout ?? ChatHubRelay.DefaultInvocationTimeout);
    }

    [Fact]
    public async Task ConnectAsync_OpensDownstreamConnection()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        await relay.ConnectAsync("frontend-1", "test-token");

        await relay.DisconnectAsync("frontend-1");
    }

    [Fact]
    public async Task SendGlobalMessage_ForwardsToDownstreamHub()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);
        var state = _app.Services.GetRequiredService<TestHubState>();

        await relay.ConnectAsync("frontend-1", "test-token");
        await relay.SendGlobalMessageAsync("frontend-1", "hello world");

        // Allow a short delay for the hub method to complete on the server side.
        await WaitForAsync(() => state.LastSendGlobalMessage == "hello world", TimeSpan.FromSeconds(2));
        state.LastSendGlobalMessage.Should().Be("hello world");

        await relay.DisconnectAsync("frontend-1");
    }

    [Fact]
    public async Task JoinGlobalChat_RelaysResponseFromDownstreamHub()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        await relay.ConnectAsync("frontend-1", "test-token");
        var response = await relay.JoinGlobalChatAsync("frontend-1");

        response.Should().NotBeNull();
        // Response is JSON — we just confirm the call returned a value.

        await relay.DisconnectAsync("frontend-1");
    }

    [Fact]
    public async Task SendDirectMessage_ReturnsDownstreamResponse()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        await relay.ConnectAsync("frontend-1", "test-token");
        var response = await relay.SendDirectMessageAsync("frontend-1", Guid.NewGuid(), "hi");

        response.Should().NotBeNull();

        await relay.DisconnectAsync("frontend-1");
    }

    [Fact]
    public async Task ServerPushedEvent_IsForwardedToFrontendVerbatim()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);
        var state = _app.Services.GetRequiredService<TestHubState>();

        await relay.ConnectAsync("frontend-1", "test-token");
        // Trigger the test hub to push a GlobalMessageReceived event back to the caller.
        await relay.SendGlobalMessageAsync("frontend-1", "trigger-broadcast");

        await WaitForAsync(
            () => sender.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == nameof(IFrontendChatSender.SendAsync)
                       && (string)c.GetArguments()[1]! == "GlobalMessageReceived"
                       && (string)c.GetArguments()[0]! == "frontend-1"),
            TimeSpan.FromSeconds(3));

        await sender.Received().SendAsync(
            "frontend-1",
            "GlobalMessageReceived",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());

        await relay.DisconnectAsync("frontend-1");
    }

    [Fact]
    public async Task DownstreamDrop_SendsChatConnectionLostToFrontend()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);
        var state = _app.Services.GetRequiredService<TestHubState>();

        await relay.ConnectAsync("frontend-2", "test-token");
        // Ask the hub to abort our connection from the server side.
        try { await relay.SendGlobalMessageAsync("frontend-2", "abort-me"); } catch { /* expected on abort */ }

        await WaitForAsync(
            () => sender.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == nameof(IFrontendChatSender.SendAsync)
                       && (string)c.GetArguments()[1]! == ChatHubRelay.ChatConnectionLostEvent),
            TimeSpan.FromSeconds(3));

        await sender.Received().SendAsync(
            "frontend-2",
            ChatHubRelay.ChatConnectionLostEvent,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HungDownstreamCall_TimesOutAndSendsChatConnectionLost()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        // 500ms timeout for fast test execution.
        var relay = CreateRelay(sender, TimeSpan.FromMilliseconds(500));

        await relay.ConnectAsync("frontend-3", "test-token");

        // SendGlobalMessage with content "hang" is configured to wait > timeout.
        Func<Task> act = () => relay.SendGlobalMessageAsync("frontend-3", "hang");
        await act.Should().ThrowAsync<TimeoutException>();

        // ChatConnectionLost (downstream_timeout) must be sent to the frontend.
        await sender.Received().SendAsync(
            "frontend-3",
            ChatHubRelay.ChatConnectionLostEvent,
            Arg.Is<object?[]>(args =>
                args.Length == 1 && args[0]!.ToString()!.Contains("downstream_timeout")),
            Arg.Any<CancellationToken>());

        // Connection tracking should have been cleaned up by the timeout handler.
        Func<Task> retry = () => relay.SendGlobalMessageAsync("frontend-3", "anything");
        await retry.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DisconnectAsync_RemovesConnection_AndIsIdempotent()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        await relay.ConnectAsync("frontend-4", "test-token");
        await relay.DisconnectAsync("frontend-4");
        // Idempotent — second call no-throw.
        await relay.DisconnectAsync("frontend-4");

        Func<Task> act = () => relay.SendGlobalMessageAsync("frontend-4", "x");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GracefulDisconnect_DoesNotEmitChatConnectionLost()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        await relay.ConnectAsync("frontend-graceful", "test-token");
        await relay.DisconnectAsync("frontend-graceful");

        // Give SignalR's Closed callback a brief moment to fire (or not).
        await Task.Delay(200);

        await sender.DidNotReceive().SendAsync(
            "frontend-graceful",
            ChatHubRelay.ChatConnectionLostEvent,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForcedReconnect_DoesNotEmitChatConnectionLost()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        // First connect, then a second ConnectAsync for the same frontend id —
        // ChatHubRelay tears down the previous downstream as part of the replace.
        // That replace must NOT surface a false ChatConnectionLost to the frontend.
        await relay.ConnectAsync("frontend-reconnect", "test-token");
        await relay.ConnectAsync("frontend-reconnect", "test-token");

        await Task.Delay(200);

        await sender.DidNotReceive().SendAsync(
            "frontend-reconnect",
            ChatHubRelay.ChatConnectionLostEvent,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());

        await relay.DisconnectAsync("frontend-reconnect");
    }

    [Fact]
    public async Task DisposeAsync_DoesNotEmitChatConnectionLost()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        await relay.ConnectAsync("frontend-dispose", "test-token");
        await relay.DisposeAsync();

        await Task.Delay(200);

        await sender.DidNotReceive().SendAsync(
            "frontend-dispose",
            ChatHubRelay.ChatConnectionLostEvent,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllTrackedConnections()
    {
        var sender = Substitute.For<IFrontendChatSender>();
        var relay = CreateRelay(sender);

        await relay.ConnectAsync("a", "t");
        await relay.ConnectAsync("b", "t");

        await relay.DisposeAsync();

        Func<Task> act = () => relay.SendGlobalMessageAsync("a", "x");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(25);
        }
    }

    /// <summary>Minimal in-process Chat hub fake. Mirrors the frozen Batch 3 method set.</summary>
    public sealed class TestChatHub : Hub
    {
        private readonly TestHubState _state;
        public TestChatHub(TestHubState state) { _state = state; }

        public async Task<object> JoinGlobalChat()
        {
            _state.JoinCount++;
            return await Task.FromResult<object>(new
            {
                ConversationId = Guid.NewGuid(),
                RecentMessages = Array.Empty<object>(),
                OnlinePlayers = Array.Empty<object>(),
                TotalOnline = 0L
            });
        }

        public Task LeaveGlobalChat() => Task.CompletedTask;

        public async Task SendGlobalMessage(string content)
        {
            _state.LastSendGlobalMessage = content;

            if (content == "trigger-broadcast")
            {
                await Clients.Caller.SendAsync("GlobalMessageReceived", new
                {
                    MessageId = Guid.NewGuid(),
                    Sender = new { PlayerId = Guid.NewGuid(), DisplayName = "Tester" },
                    Content = "broadcasted",
                    SentAt = DateTimeOffset.UtcNow
                });
            }
            else if (content == "abort-me")
            {
                Context.Abort();
            }
            else if (content == "hang")
            {
                // Wait long enough that the relay's invocation timeout fires.
                await Task.Delay(TimeSpan.FromSeconds(10), Context.ConnectionAborted);
            }
        }

        public Task<object> SendDirectMessage(Guid recipientPlayerId, string content)
        {
            return Task.FromResult<object>(new
            {
                ConversationId = Guid.NewGuid(),
                MessageId = Guid.NewGuid(),
                SentAt = DateTimeOffset.UtcNow
            });
        }
    }

    public sealed class TestHubState
    {
        public string? LastSendGlobalMessage;
        public int JoinCount;
    }
}
