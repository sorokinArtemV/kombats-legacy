using System.Diagnostics;
using System.Net.Mime;

namespace Kombats.Players.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = MediaTypeNames.Application.Json;

                string traceId = Activity.Current?.Id ?? context.TraceIdentifier;

                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                    title = "InternalServerError",
                    status = 500,
                    detail = "An unexpected error occurred.",
                    traceId
                }, context.RequestAborted);
            }
        }
    }
}
