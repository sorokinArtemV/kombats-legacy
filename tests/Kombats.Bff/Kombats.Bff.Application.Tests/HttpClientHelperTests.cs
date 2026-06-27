using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Bff.Application.Tests;

public sealed class HttpClientHelperTests
{
    [Fact]
    public async Task SendAsync_SuccessfulGet_ReturnsDeserializedResponse()
    {
        var expected = new TestDto("hello");
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        TestDto? result = await HttpClientHelper.SendAsync<TestDto>(
            httpClient, HttpMethod.Get, "/test", null, "TestService",
            NullLogger.Instance, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Value.Should().Be("hello");
    }

    [Fact]
    public async Task SendAsync_NotFound_ReturnsNull()
    {
        using var handler = new StubHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        TestDto? result = await HttpClientHelper.SendAsync<TestDto>(
            httpClient, HttpMethod.Get, "/test", null, "TestService",
            NullLogger.Instance, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ServerError_ThrowsBffServiceException()
    {
        using var handler = new StubHandler(HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        Func<Task> act = () => HttpClientHelper.SendAsync<TestDto>(
            httpClient, HttpMethod.Get, "/test", null, "TestService",
            NullLogger.Instance, CancellationToken.None);

        await act.Should().ThrowAsync<BffServiceException>()
            .Where(e => e.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task SendAsync_HttpRequestException_ThrowsServiceUnavailableException()
    {
        using var handler = new ThrowingHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        Func<Task> act = () => HttpClientHelper.SendAsync<TestDto>(
            httpClient, HttpMethod.Get, "/test", null, "TestService",
            NullLogger.Instance, CancellationToken.None);

        await act.Should().ThrowAsync<ServiceUnavailableException>()
            .Where(e => e.ServiceName == "TestService");
    }

    [Fact]
    public async Task SendAsync_WithBody_SendsJsonContent()
    {
        var expected = new TestDto("ok");
        using var handler = new StubHandler(HttpStatusCode.OK, expected);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        await HttpClientHelper.SendAsync<TestDto>(
            httpClient, HttpMethod.Post, "/test", new { Name = "test" }, "TestService",
            NullLogger.Instance, CancellationToken.None);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequestBody.Should().Contain("test");
    }

    public sealed record TestDto(string Value);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _responseBody;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHandler(HttpStatusCode statusCode, object? responseBody = null)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(_statusCode);
            if (_responseBody is not null)
            {
                response.Content = JsonContent.Create(_responseBody);
            }
            else
            {
                response.Content = new StringContent("");
            }

            return response;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }
}
