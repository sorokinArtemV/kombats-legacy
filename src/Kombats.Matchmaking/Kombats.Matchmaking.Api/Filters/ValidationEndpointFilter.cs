using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Kombats.Matchmaking.Api.Filters;

internal sealed class RequestValidationFilter<TRequest> : IEndpointFilter where TRequest : notnull
{
    private readonly IValidator<TRequest>? _validator;

    public RequestValidationFilter(IValidator<TRequest>? validator = null)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (_validator is null) return await next(context);

        TRequest? request = default;
        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is TRequest typed)
            {
                request = typed;
                break;
            }
        }

        if (request is null) return await next(context);

        var result = await _validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (result.IsValid) return await next(context);

        var errors = result.Errors
            .GroupBy(e => string.IsNullOrWhiteSpace(e.PropertyName) ? "Request" : e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return Results.Problem(
            title: "ValidationFailed",
            detail: "One or more validation errors occurred.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["errors"] = errors });
    }
}

internal static class EndpointValidationExtensions
{
    public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : notnull
        => builder.AddEndpointFilter<RequestValidationFilter<TRequest>>();
}
