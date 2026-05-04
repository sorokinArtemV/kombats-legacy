using Kombats.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Kombats.Matchmaking.Api.Extensions;

public static class ResultExtensions
{
    public static TOut Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<Result<TIn>, TOut> onFailure)
    {
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result);
    }

    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess) throw new InvalidOperationException();

        var error = result.Error;
        var statusCode = error.Type switch
        {
            ErrorType.Validation or ErrorType.Problem => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: statusCode);
    }
}
