using System.Diagnostics;
using Kombats.Bff.Application.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BffServiceException ex)
        {
            logger.LogWarning(ex, "BFF service exception: {ErrorCode} — {ErrorMessage}",
                ex.Error.Code, ex.Error.Message);

            await WriteErrorResponseAsync(context, (int)ex.StatusCode, ex.Error);
        }
        catch (ServiceUnavailableException ex)
        {
            logger.LogWarning(ex, "Service unavailable: {ServiceName}", ex.ServiceName);

            var error = new BffError(BffErrorCode.ServiceUnavailable, ex.Message);
            await WriteErrorResponseAsync(context, StatusCodes.Status503ServiceUnavailable, error);
        }
        catch (BadHttpRequestException ex)
        {
            logger.LogWarning(ex, "Bad HTTP request");

            var error = new BffError(BffErrorCode.InvalidRequest, "Invalid request format.");
            await WriteErrorResponseAsync(context, StatusCodes.Status400BadRequest, error);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — no response needed
            logger.LogDebug("Request cancelled by client");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing request {Method} {Path}",
                context.Request.Method, context.Request.Path);

            var error = new BffError(BffErrorCode.InternalError, "An unexpected error occurred.");
            await WriteErrorResponseAsync(context, StatusCodes.Status500InternalServerError, error);
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context, int statusCode, BffError error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        string? traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new BffErrorResponse(error with { Details = new { traceId } }));
    }
}
