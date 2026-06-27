using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Application.Errors;

public static class ErrorMapper
{
    private const int BodySnippetMaxLength = 512;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<BffError> MapFromResponseAsync(
        HttpResponseMessage response,
        string serviceName,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        string? body = null;

        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // If we can't read the body, fall through to status-based mapping
        }

        BffError error = response.StatusCode switch
        {
            HttpStatusCode.NotFound => new BffError(BffErrorCode.CharacterNotFound, $"Resource not found in {serviceName}."),
            HttpStatusCode.Conflict => MapConflictError(body, serviceName),
            HttpStatusCode.BadRequest => MapBadRequestError(body, serviceName),
            HttpStatusCode.Unauthorized => new BffError(BffErrorCode.Unauthorized, "Authentication required."),
            HttpStatusCode.Forbidden => new BffError(BffErrorCode.Unauthorized, "Access denied."),
            HttpStatusCode.ServiceUnavailable => new BffError(BffErrorCode.ServiceUnavailable, $"{serviceName} service is unavailable."),
            _ => new BffError(BffErrorCode.InternalError, $"Unexpected error from {serviceName} (HTTP {(int)response.StatusCode}).")
        };

        // Single structured boundary log for every non-2xx downstream response mapped
        // into a BffError. Keeps the upstream error surface debuggable without
        // requiring call sites to each decide what to log.
        string? bodySnippet = body is null
            ? null
            : (body.Length > BodySnippetMaxLength ? body[..BodySnippetMaxLength] : body);

        string? requestPath = response.RequestMessage?.RequestUri?.PathAndQuery;
        string? requestMethod = response.RequestMessage?.Method.Method;

        logger.LogWarning(
            "Downstream {DownstreamService} returned non-success {StatusCode} for {RequestMethod} {RequestPath}. ErrorCode={ErrorCode} Body={ResponseBody}",
            serviceName,
            (int)response.StatusCode,
            requestMethod,
            requestPath,
            error.Code,
            bodySnippet);

        return error;
    }

    private static BffError MapConflictError(string? body, string serviceName)
    {
        if (body is not null && body.Contains("queue", StringComparison.OrdinalIgnoreCase))
        {
            return new BffError(BffErrorCode.AlreadyInQueue, "Player is already in a queue or match.");
        }

        return new BffError(BffErrorCode.InvalidRequest, $"Conflict in {serviceName}.");
    }

    private static BffError MapBadRequestError(string? body, string serviceName)
    {
        if (body is not null)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("errors", out JsonElement errors))
                {
                    return new BffError(BffErrorCode.InvalidRequest, "Validation failed.", errors.Clone());
                }

                if (root.TryGetProperty("message", out JsonElement message))
                {
                    return new BffError(BffErrorCode.InvalidRequest, message.GetString() ?? "Invalid request.");
                }
            }
            catch (JsonException)
            {
                // Not JSON — fall through
            }
        }

        return new BffError(BffErrorCode.InvalidRequest, $"Invalid request to {serviceName}.");
    }
}
