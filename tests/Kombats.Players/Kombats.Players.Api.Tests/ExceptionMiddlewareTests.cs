using System.Net;
using System.Text.Json;
using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Api.Tests.Fixtures;
using Kombats.Players.Application;
using Kombats.Players.Application.UseCases.GetCharacter;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Players.Api.Tests;

public sealed class ExceptionMiddlewareTests : IClassFixture<PlayersApiFactory>, IDisposable
{
    private readonly PlayersApiFactory _factory;
    private readonly HttpClient _client;

    public ExceptionMiddlewareTests(PlayersApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = true;
        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task UnhandledException_Returns500WithTraceId()
    {
        // Configure the handler to throw an unhandled exception
        _factory.GetCharacterHandler
            .HandleAsync(Arg.Any<GetCharacterQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Unexpected test error"));

        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("title").GetString().Should().Be("InternalServerError");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        doc.RootElement.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        doc.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();

        // Must NOT contain stack trace or internal details
        body.Should().NotContain("Unexpected test error");
        body.Should().NotContain("InvalidOperationException");
    }
}
