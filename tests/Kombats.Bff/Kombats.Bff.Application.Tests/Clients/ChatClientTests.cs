using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Errors;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Bff.Application.Tests.Clients;

public sealed class ChatClientTests
{
    [Fact]
    public async Task GetConversationsAsync_HitsExpectedPath()
    {
        var expected = new InternalConversationListResponse(new List<InternalConversationDto>());
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        var result = await client.GetConversationsAsync();

        result.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be("/api/internal/conversations");
    }

    [Fact]
    public async Task GetConversationsAsync_ReturnsNull_OnNotFound()
    {
        using var handler = new StubHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        (await client.GetConversationsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetMessagesAsync_BuildsBeforeAndLimit()
    {
        var expected = new InternalMessageListResponse(new List<InternalChatMessageDto>(), false);
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        var conversationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var before = new DateTimeOffset(2026, 04, 15, 12, 30, 45, TimeSpan.Zero);

        await client.GetMessagesAsync(conversationId, before, 25);

        string actual = handler.LastRequest!.RequestUri!.PathAndQuery;
        actual.Should().StartWith($"/api/internal/conversations/{conversationId}/messages?");
        actual.Should().Contain("before=");
        actual.Should().Contain("limit=25");
    }

    [Fact]
    public async Task GetMessagesAsync_OmitsBeforeWhenNull()
    {
        var expected = new InternalMessageListResponse(new List<InternalChatMessageDto>(), false);
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        await client.GetMessagesAsync(Guid.NewGuid(), before: null, limit: 50);

        string actual = handler.LastRequest!.RequestUri!.PathAndQuery;
        actual.Should().NotContain("before=");
        actual.Should().Contain("limit=50");
    }

    [Fact]
    public async Task GetDirectMessagesAsync_HitsExpectedPath()
    {
        var expected = new InternalMessageListResponse(new List<InternalChatMessageDto>(), false);
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        var other = Guid.Parse("99999999-0000-1111-2222-333333333333");
        await client.GetDirectMessagesAsync(other, before: null, limit: 50);

        handler.LastRequest!.RequestUri!.PathAndQuery
            .Should().StartWith($"/api/internal/direct/{other}/messages?");
    }

    [Fact]
    public async Task GetOnlinePlayersAsync_HitsExpectedPath()
    {
        var expected = new InternalOnlinePlayersResponse(new List<InternalOnlinePlayerDto>(), 0);
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        var client = CreateClient(handler);

        await client.GetOnlinePlayersAsync(limit: 100, offset: 50);

        handler.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be("/api/internal/presence/online?limit=100&offset=50");
    }

    [Fact]
    public async Task ChatClient_ThrowsServiceUnavailable_OnNetworkFailure()
    {
        using var handler = new ThrowingHandler();
        var client = CreateClient(handler);

        Func<Task> act = () => client.GetConversationsAsync();
        await act.Should().ThrowAsync<ServiceUnavailableException>()
            .Where(e => e.ServiceName == "Chat");
    }

    [Fact]
    public async Task ChatClient_ThrowsBffServiceException_OnServerError()
    {
        using var handler = new StubHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        Func<Task> act = () => client.GetConversationsAsync();
        await act.Should().ThrowAsync<BffServiceException>()
            .Where(e => e.StatusCode == HttpStatusCode.InternalServerError);
    }

    private static ChatClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
        return new ChatClient(http, NullLogger<ChatClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode statusCode, object? body = null)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_statusCode);
            response.Content = _body is null
                ? new StringContent("")
                : JsonContent.Create(_body);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Connection refused");
    }
}
