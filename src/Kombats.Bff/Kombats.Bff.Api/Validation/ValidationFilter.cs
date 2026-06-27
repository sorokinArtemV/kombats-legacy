using FluentValidation;
using Kombats.Bff.Application.Errors;
using Microsoft.AspNetCore.Http;

namespace Kombats.Bff.Api.Validation;

public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        T? argument = context.Arguments.OfType<T>().FirstOrDefault();

        if (argument is null)
        {
            return Results.BadRequest(new BffErrorResponse(
                new BffError(BffErrorCode.InvalidRequest, "Request body is required.")));
        }

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(argument);

        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            return Results.BadRequest(new BffErrorResponse(
                new BffError(BffErrorCode.InvalidRequest, "Validation failed.", errors)));
        }

        return await next(context);
    }
}
