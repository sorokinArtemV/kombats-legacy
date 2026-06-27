using System.Net;
using FluentAssertions;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Kombats.Bff.Application.Tests;

public sealed class JwtForwardingHandlerTests
{
    [Fact]
    public async Task SendAsync_CopiesAuthorizationHeader_WhenPresent()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer test-jwt-token";
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new JwtForwardingHandler(httpContextAccessor)
        {
            InnerHandler = new RecordingHandler()
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        // Act
        await client.SendAsync(request);

        // Assert
        var recorded = ((RecordingHandler)handler.InnerHandler).LastRequest;
        recorded.Should().NotBeNull();
        recorded!.Headers.Authorization.Should().NotBeNull();
        recorded.Headers.Authorization!.Scheme.Should().Be("Bearer");
        recorded.Headers.Authorization.Parameter.Should().Be("test-jwt-token");
    }

    [Fact]
    public async Task SendAsync_DoesNotAddAuthorizationHeader_WhenNotPresent()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new JwtForwardingHandler(httpContextAccessor)
        {
            InnerHandler = new RecordingHandler()
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        // Act
        await client.SendAsync(request);

        // Assert
        var recorded = ((RecordingHandler)handler.InnerHandler).LastRequest;
        recorded.Should().NotBeNull();
        recorded!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_DoesNotAddAuthorizationHeader_WhenNoHttpContext()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var handler = new JwtForwardingHandler(httpContextAccessor)
        {
            InnerHandler = new RecordingHandler()
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        // Act
        await client.SendAsync(request);

        // Assert
        var recorded = ((RecordingHandler)handler.InnerHandler).LastRequest;
        recorded.Should().NotBeNull();
        recorded!.Headers.Authorization.Should().BeNull();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
