using System.Net;
using System.Net.Http.Json;
using Kombats.Abstractions;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.GetConversations;
using Kombats.Chat.Application.UseCases.GetDirectMessages;
using Kombats.Chat.Api.Tests.Fixtures;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Api.Tests.Endpoints;

public sealed class ConversationsEndpointTests : IClassFixture<ChatApiFactory>
{
    private readonly ChatApiFactory _factory;

    public ConversationsEndpointTests(ChatApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = true;
    }

    [Fact]
    public async Task GetConversations_Authenticated_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/internal/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetConversations_Unauthenticated_Returns401()
    {
        _factory.AuthenticateRequests = false;
        try
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/internal/conversations");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            _factory.AuthenticateRequests = true;
        }
    }

    [Fact]
    public async Task GetConversationMessages_NonexistentConversation_Returns404()
    {
        var client = _factory.CreateClient();
        var nonexistentId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/internal/conversations/{nonexistentId}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetConversationMessages_Unauthenticated_Returns401()
    {
        _factory.AuthenticateRequests = false;
        try
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync($"/api/internal/conversations/{Guid.NewGuid()}/messages");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            _factory.AuthenticateRequests = true;
        }
    }

    [Fact]
    public async Task GetDirectMessages_Unauthenticated_Returns401()
    {
        _factory.AuthenticateRequests = false;
        try
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync($"/api/internal/direct/{Guid.NewGuid()}/messages");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            _factory.AuthenticateRequests = true;
        }
    }

    [Fact]
    public async Task GetDirectMessages_SameUser_Returns400()
    {
        var client = _factory.CreateClient();
        // The test auth handler uses TestSubjectId as the "sub" claim
        var selfId = Guid.Parse(ChatApiFactory.TestSubjectId);

        var response = await client.GetAsync($"/api/internal/direct/{selfId}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
