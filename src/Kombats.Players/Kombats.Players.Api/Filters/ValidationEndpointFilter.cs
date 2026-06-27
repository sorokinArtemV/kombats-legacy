using FluentValidation;
using FluentValidation.Results;

namespace Kombats.Players.Api.Filters;

/// <summary>
/// Validates a single request DTO (TRequest) using FluentValidation before executing the endpoint.
/// No reflection, no argument scanning.
/// </summary>
internal sealed class RequestValidationFilter<TRequest> : IEndpointFilter where TRequest : notnull
{
    private readonly IValidator<TRequest>? _validator;
    
    public RequestValidationFilter(IValidator<TRequest>? validator = null)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (_validator is null)
        {
            return await next(context).ConfigureAwait(false);
        }
        
        TRequest? request = default;

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is TRequest typed)
            {
                request = typed;
                break;
            }
        }

        if (request is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        ValidationResult result = await _validator
            .ValidateAsync(request, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (result.IsValid)
        {
            return await next(context).ConfigureAwait(false);
        }


        var errors = result.Errors
            .Where(e => e is not null)
            .GroupBy(e => string.IsNullOrWhiteSpace(e.PropertyName) ? "Request" : e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ErrorMessage)
                      .Where(m => !string.IsNullOrWhiteSpace(m))
                      .Distinct(StringComparer.Ordinal)
                      .ToArray(),
                StringComparer.Ordinal);

        return Results.Problem(
            title: "ValidationFailed",
            detail: "One or more validation errors occurred.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errors"] = errors
            });
    }
}

internal static class EndpointValidationExtensions
{
    /// <summary>
    /// Adds request validation filter for the specified request DTO type.
    /// </summary>
    public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : notnull
    {
        return builder.AddEndpointFilter<RequestValidationFilter<TRequest>>();
    }
}


