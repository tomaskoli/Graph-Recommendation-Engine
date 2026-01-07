using FluentResults;
using Recommendation.Api.Common.Errors;

namespace Recommendation.Api.Common.Extensions;

public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var firstError = result.Errors.FirstOrDefault();

        return firstError switch
        {
            NotFoundError => Results.NotFound(firstError.Message),
            ValidationError => Results.BadRequest(firstError.Message),
            _ => Results.Problem(
                detail: firstError?.Message ?? "An unexpected error occurred",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}

