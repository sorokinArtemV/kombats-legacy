using FluentAssertions;
using Kombats.Chat.Api.Tests.Fixtures;
using Kombats.Chat.Application;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.JoinGlobalChat;
using Kombats.Chat.Application.UseCases.SendDirectMessage;
using Kombats.Chat.Domain.Conversations;
using Kombats.Chat.Domain.Messages;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Kombats.Chat.Api.Tests.Hubs;

public sealed class InternalChatHubTests : IClassFixture<ChatHubFactory>, IAsyncLifetime
{
    private readonly ChatHubFactory _factory;

    public InternalChatHubTests(ChatHubFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _factory.AuthenticateRequests = true;
        _factory.CallerIdentityId = Guid.NewGuid();
        _factory.Presence.ClearSubstitute();
        _factory.RateLimiter.ClearSubstitute();
        _factory.Eligibility.ClearSubstitute();
        _factory.DisplayNames.ClearSubstitute();
        _factory.Conversations.ClearSubstitute();
        _factory.Messages.ClearSubstitute();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubConnection BuildConnection(bool authenticated = true)
    {
        _factory.AuthenticateRequests = authenticated;
        var server = _factory.Server;
        var conn = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "chathub-internal"), opts =>
            {
                opts.HttpMessageHandlerFactory = _ => server.CreateHandler();
                opts.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        return conn;
    }

    [Fact]
    public async Task Connect_Unauthenticated_Fails()
    {
        await using var conn = BuildConnection(authenticated: false);
        Func<Task> act = () => conn.StartAsync();
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Connect_Authenticated_Succeeds()
    {
        _factory.DisplayNames.ResolveAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>()).Returns("Alice");
        _factory.Presence.ConnectAsync(_factory.CallerIdentityId, "Alice", Arg.Any<CancellationToken>()).Returns(false);

        await using var conn = BuildConnection();
        await conn.StartAsync();

        conn.State.Should().Be(HubConnectionState.Connected);
        await _factory.Presence.Received().ConnectAsync(_factory.CallerIdentityId, "Alice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinGlobalChat_Eligible_ReturnsResponse()
    {
        SetupConnect();
        _factory.Eligibility.CheckEligibilityAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _factory.Messages.GetByConversationAsync(Conversation.GlobalConversationId, null, JoinGlobalChatHandler.RecentMessagesLimit, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());
        _factory.Presence.GetOnlinePlayersAsync(JoinGlobalChatHandler.OnlinePlayersInitialLimit, 0, Arg.Any<CancellationToken>())
            .Returns(new List<OnlinePlayer>());
        _factory.Presence.GetOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        await using var conn = BuildConnection();
        await conn.StartAsync();

        var response = await conn.InvokeAsync<JoinGlobalChatResponse>("JoinGlobalChat");

        response.ConversationId.Should().Be(Conversation.GlobalConversationId);
    }

    [Fact]
    public async Task JoinGlobalChat_NamedButNotReady_EmitsChatError()
    {
        // Critical Batch 3 negative case at hub level.
        SetupConnect();
        _factory.Eligibility.CheckEligibilityAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(false));

        await using var conn = BuildConnection();
        var errorTcs = new TaskCompletionSource<ChatErrorEvent>();
        conn.On<ChatErrorEvent>("ChatError", e => errorTcs.TrySetResult(e));

        await conn.StartAsync();
        var response = await conn.InvokeAsync<JoinGlobalChatResponse?>("JoinGlobalChat");

        response.Should().BeNull();
        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.Should().Be(ChatErrorCodes.NotEligible);
    }

    [Fact]
    public async Task SendGlobalMessage_HappyPath_BroadcastsToGroup()
    {
        SetupConnect();
        _factory.Eligibility.CheckEligibilityAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _factory.RateLimiter.CheckAndIncrementAsync(_factory.CallerIdentityId, "global", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true));
        _factory.Messages.GetByConversationAsync(Conversation.GlobalConversationId, null, JoinGlobalChatHandler.RecentMessagesLimit, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());
        _factory.Presence.GetOnlinePlayersAsync(JoinGlobalChatHandler.OnlinePlayersInitialLimit, 0, Arg.Any<CancellationToken>())
            .Returns(new List<OnlinePlayer>());
        _factory.Presence.GetOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        await using var conn = BuildConnection();
        var msgTcs = new TaskCompletionSource<GlobalMessageEvent>();
        conn.On<GlobalMessageEvent>("GlobalMessageReceived", e => msgTcs.TrySetResult(e));

        await conn.StartAsync();
        await conn.InvokeAsync<JoinGlobalChatResponse?>("JoinGlobalChat");
        await conn.InvokeAsync("SendGlobalMessage", "hello world");

        var msg = await msgTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        msg.Content.Should().Be("hello world");
        msg.Sender.PlayerId.Should().Be(_factory.CallerIdentityId);
    }

    [Fact]
    public async Task SendGlobalMessage_RateLimited_EmitsChatErrorWithRetryAfter()
    {
        SetupConnect();
        _factory.Eligibility.CheckEligibilityAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _factory.RateLimiter.CheckAndIncrementAsync(_factory.CallerIdentityId, "global", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(false, 4321));

        await using var conn = BuildConnection();
        var errorTcs = new TaskCompletionSource<ChatErrorEvent>();
        conn.On<ChatErrorEvent>("ChatError", e => errorTcs.TrySetResult(e));

        await conn.StartAsync();
        await conn.InvokeAsync("SendGlobalMessage", "hi");

        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.Should().Be(ChatErrorCodes.RateLimited);
        error.RetryAfterMs.Should().Be(4321);
    }

    [Fact]
    public async Task SendGlobalMessage_InvalidContent_EmitsChatError()
    {
        SetupConnect();
        _factory.Eligibility.CheckEligibilityAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _factory.RateLimiter.CheckAndIncrementAsync(_factory.CallerIdentityId, "global", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true));

        await using var conn = BuildConnection();
        var errorTcs = new TaskCompletionSource<ChatErrorEvent>();
        conn.On<ChatErrorEvent>("ChatError", e => errorTcs.TrySetResult(e));

        await conn.StartAsync();
        await conn.InvokeAsync("SendGlobalMessage", "   "); // empty after sanitization

        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.Should().Be(ChatErrorCodes.MessageEmpty);
    }

    [Fact]
    public async Task SendDirectMessage_HappyPath_DeliversToRecipientGroup()
    {
        var sender = _factory.CallerIdentityId;
        var recipient = Guid.NewGuid();
        SetupConnect();
        _factory.Eligibility.CheckEligibilityAsync(sender, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Alice"));
        _factory.Eligibility.CheckEligibilityAsync(recipient, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Bob"));
        _factory.RateLimiter.CheckAndIncrementAsync(sender, "dm", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true));
        var conversation = Conversation.CreateDirect(sender, recipient);
        _factory.Conversations.GetOrCreateDirectAsync(sender, recipient, Arg.Any<CancellationToken>())
            .Returns(conversation);

        // Sender sends; we observe the call into the notifier by spying on the recipient group via a second connection.
        // Simpler: assert handler effect through Messages.SaveAsync invocation.
        await using var conn = BuildConnection();
        await conn.StartAsync();

        var resp = await conn.InvokeAsync<SendDirectMessageResponse>("SendDirectMessage", recipient, "hey");

        resp.ConversationId.Should().Be(conversation.Id);
        await _factory.Messages.Received(1).SaveAsync(
            Arg.Is<Message>(m => m.ConversationId == conversation.Id && m.SenderIdentityId == sender),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disconnect_LastConnection_BroadcastsPlayerOffline()
    {
        SetupConnect();
        // Simulate first connect → last disconnect.
        _factory.Presence.ConnectAsync(_factory.CallerIdentityId, "Alice", Arg.Any<CancellationToken>()).Returns(true);
        _factory.Presence.DisconnectAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>()).Returns(true);

        await using (var conn = BuildConnection())
        {
            await conn.StartAsync();
            await conn.StopAsync();
        }

        // Allow async disconnect handler to run.
        await Task.Delay(100);
        await _factory.Presence.Received().DisconnectAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveGlobalChat_SilenceFollowingGlobalBroadcasts()
    {
        // Two clients: A joins, then leaves. B joins and stays. A sends nothing.
        // After A leaves, when B sends a global message, A must NOT receive GlobalMessageReceived.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Defaults that allow both to connect, join, and send.
        _factory.DisplayNames.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("User");
        _factory.Presence.ConnectAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _factory.Eligibility.CheckEligibilityAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "User"));
        _factory.RateLimiter.CheckAndIncrementAsync(Arg.Any<Guid>(), "global", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true));
        _factory.Messages.GetByConversationAsync(Conversation.GlobalConversationId, null, JoinGlobalChatHandler.RecentMessagesLimit, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());
        _factory.Presence.GetOnlinePlayersAsync(JoinGlobalChatHandler.OnlinePlayersInitialLimit, 0, Arg.Any<CancellationToken>())
            .Returns(new List<OnlinePlayer>());
        _factory.Presence.GetOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(0);

        _factory.CallerIdentityId = a;
        await using var connA = BuildConnection();
        var aReceived = new TaskCompletionSource<GlobalMessageEvent>();
        connA.On<GlobalMessageEvent>("GlobalMessageReceived", e => aReceived.TrySetResult(e));
        await connA.StartAsync();
        await connA.InvokeAsync<JoinGlobalChatResponse?>("JoinGlobalChat");
        await connA.InvokeAsync("LeaveGlobalChat");

        _factory.CallerIdentityId = b;
        await using var connB = BuildConnection();
        await connB.StartAsync();
        await connB.InvokeAsync<JoinGlobalChatResponse?>("JoinGlobalChat");
        await connB.InvokeAsync("SendGlobalMessage", "after-leave");

        // Wait briefly; A must not have received anything.
        var completed = await Task.WhenAny(aReceived.Task, Task.Delay(750));
        completed.Should().NotBe(aReceived.Task, "A left the global group and must no longer receive global broadcasts");
    }

    [Fact]
    public async Task SendDirectMessage_RecipientReceivesEventOnSecondConnection()
    {
        // Two real authenticated hub connections: sender and recipient. Recipient
        // joins its per-identity DM group automatically on connect. Sender invokes
        // SendDirectMessage; recipient must receive DirectMessageReceived end-to-end.
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        _factory.DisplayNames.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("User");
        _factory.Presence.ConnectAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _factory.Eligibility.CheckEligibilityAsync(sender, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Sender"));
        _factory.Eligibility.CheckEligibilityAsync(recipient, Arg.Any<CancellationToken>())
            .Returns(new EligibilityResult(true, "Recipient"));
        _factory.RateLimiter.CheckAndIncrementAsync(sender, "dm", Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true));
        var conversation = Conversation.CreateDirect(sender, recipient);
        _factory.Conversations.GetOrCreateDirectAsync(sender, recipient, Arg.Any<CancellationToken>())
            .Returns(conversation);

        // Recipient connects first so its identity group is established before the send.
        _factory.CallerIdentityId = recipient;
        await using var recvConn = BuildConnection();
        var receivedTcs = new TaskCompletionSource<DirectMessageEvent>();
        recvConn.On<DirectMessageEvent>("DirectMessageReceived", e => receivedTcs.TrySetResult(e));
        await recvConn.StartAsync();

        // Sender connects and invokes SendDirectMessage.
        _factory.CallerIdentityId = sender;
        await using var sendConn = BuildConnection();
        await sendConn.StartAsync();
        var resp = await sendConn.InvokeAsync<SendDirectMessageResponse>("SendDirectMessage", recipient, "ping");

        resp.ConversationId.Should().Be(conversation.Id);

        var dm = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        dm.Content.Should().Be("ping");
        dm.ConversationId.Should().Be(conversation.Id);
        dm.Sender.PlayerId.Should().Be(sender);
    }

    private void SetupConnect()
    {
        _factory.DisplayNames.ResolveAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>()).Returns("Alice");
        _factory.Presence.ConnectAsync(_factory.CallerIdentityId, "Alice", Arg.Any<CancellationToken>()).Returns(false);
        _factory.Presence.DisconnectAsync(_factory.CallerIdentityId, Arg.Any<CancellationToken>()).Returns(false);
    }
}
