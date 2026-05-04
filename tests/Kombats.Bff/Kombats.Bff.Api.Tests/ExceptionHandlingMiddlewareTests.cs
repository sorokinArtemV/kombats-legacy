using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Kombats.Bff.Api.Middleware;
using Kombats.Bff.Application.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class ExceptionHandlingMiddlewareTests
{
    private readonly ExceptionHandlingMiddleware _middleware;

    public ExceptionHandlingMiddlewareTests()
    {
        // The middleware is constructed with a placeholder next delegate;
        // each test provides its own pipeline via InvokeAsync override pattern.
        _middleware = new ExceptionHandlingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<ExceptionHandlingMiddleware>.Instance);
    }

    [Fact]
    public async Task BffServiceException_ReturnsMatchingStatusCodeAndError()
    {
        var error = new BffError(BffErrorCode.CharacterNotFound, "Not found");
        var exception = new BffServiceException(HttpStatusCode.NotFound, error);
        var middleware = CreateMiddleware(_ => throw exception);

        HttpContext context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(404);
        context.Response.ContentType.Should().Be("application/json; charset=utf-8");

        BffErrorResponse? response = await ReadResponseAsync(context);
        response.Should().NotBeNull();
        response!.Error.Code.Should().Be(BffErrorCode.CharacterNotFound);
        response.Error.Message.Should().Be("Not found");
    }

    [Fact]
    public async Task ServiceUnavailableException_Returns503()
    {
        var exception = new ServiceUnavailableException("Players");
        var middleware = CreateMiddleware(_ => throw exception);

        HttpContext context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(503);

        BffErrorResponse? response = await ReadResponseAsync(context);
        response.Should().NotBeNull();
        response!.Error.Code.Should().Be(BffErrorCode.ServiceUnavailable);
        response.Error.Message.Should().Contain("Players");
    }

    [Fact]
    public async Task UnhandledException_Returns500WithInternalError()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Something broke"));

        HttpContext context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);

        BffErrorResponse? response = await ReadResponseAsync(context);
        response.Should().NotBeNull();
        response!.Error.Code.Should().Be(BffErrorCode.InternalError);
        response.Error.Message.Should().NotContain("Something broke",
            "internal exception details must not leak to client");
    }

    [Fact]
    public async Task BadHttpRequestException_Returns400()
    {
        var middleware = CreateMiddleware(_ =>
            throw new BadHttpRequestException("Invalid content type"));

        HttpContext context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);

        BffErrorResponse? response = await ReadResponseAsync(context);
        response.Should().NotBeNull();
        response!.Error.Code.Should().Be(BffErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task ErrorResponse_IncludesTraceId()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("test"));

        HttpContext context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        BffErrorResponse? response = await ReadResponseAsync(context);
        response.Should().NotBeNull();
        response!.Error.Details.Should().NotBeNull();

        string detailsJson = JsonSerializer.Serialize(response.Error.Details);
        detailsJson.Should().Contain("traceId");
    }

    [Fact]
    public async Task SuccessfulRequest_PassesThrough()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        HttpContext context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);
    }

    private static HttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/test";
        return context;
    }

    private static async Task<BffErrorResponse?> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<BffErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
