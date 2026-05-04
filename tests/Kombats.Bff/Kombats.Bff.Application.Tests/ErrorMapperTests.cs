using System.Net;
using System.Text;
using FluentAssertions;
using Kombats.Bff.Application.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kombats.Bff.Application.Tests;

public sealed class ErrorMapperTests
{
    [Fact]
    public async Task MapFromResponseAsync_NotFound_ReturnsCharacterNotFound()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        response.Content = new StringContent("");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Players", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.CharacterNotFound);
    }

    [Fact]
    public async Task MapFromResponseAsync_Unauthorized_ReturnsUnauthorized()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Content = new StringContent("");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Players", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.Unauthorized);
    }

    [Fact]
    public async Task MapFromResponseAsync_Forbidden_ReturnsUnauthorized()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Content = new StringContent("");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Players", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.Unauthorized);
    }

    [Fact]
    public async Task MapFromResponseAsync_ServiceUnavailable_ReturnsServiceUnavailable()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Content = new StringContent("");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Matchmaking", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.ServiceUnavailable);
        error.Message.Should().Contain("Matchmaking");
    }

    [Fact]
    public async Task MapFromResponseAsync_ConflictWithQueueBody_ReturnsAlreadyInQueue()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Content = new StringContent("{\"message\":\"Already in queue\"}",
            Encoding.UTF8, "application/json");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Matchmaking", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.AlreadyInQueue);
    }

    [Fact]
    public async Task MapFromResponseAsync_ConflictWithoutQueueBody_ReturnsInvalidRequest()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Content = new StringContent("{\"message\":\"Concurrency violation\"}",
            Encoding.UTF8, "application/json");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Players", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task MapFromResponseAsync_BadRequestWithErrors_ReturnsValidationDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Content = new StringContent(
            "{\"errors\":{\"Name\":[\"Name is required.\"]}}",
            Encoding.UTF8, "application/json");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Players", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.InvalidRequest);
        error.Message.Should().Be("Validation failed.");
        error.Details.Should().NotBeNull();
    }

    [Fact]
    public async Task MapFromResponseAsync_BadRequestWithMessage_ReturnsMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Content = new StringContent(
            "{\"message\":\"Character not ready\"}",
            Encoding.UTF8, "application/json");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Players", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.InvalidRequest);
        error.Message.Should().Be("Character not ready");
    }

    [Fact]
    public async Task MapFromResponseAsync_BadRequestWithNonJson_ReturnsFallback()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Content = new StringContent("not json");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Players", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.InvalidRequest);
        error.Message.Should().Contain("Players");
    }

    [Fact]
    public async Task MapFromResponseAsync_UnknownStatusCode_ReturnsInternalError()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        response.Content = new StringContent("");

        BffError error = await ErrorMapper.MapFromResponseAsync(response, "Battle", NullLogger.Instance);

        error.Code.Should().Be(BffErrorCode.InternalError);
        error.Message.Should().Contain("500");
    }
}
