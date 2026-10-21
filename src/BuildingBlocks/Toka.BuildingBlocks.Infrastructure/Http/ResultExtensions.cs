using Microsoft.AspNetCore.Http;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;

namespace Toka.BuildingBlocks.Infrastructure.Http;

/// <summary>Mapea Result/Error de Application a respuestas HTTP homogéneas (ProblemDetails).</summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess
            ? (onSuccess is null ? Results.Ok(result.Value) : onSuccess(result.Value))
            : result.Error!.ToProblem();

    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            statusCode: status,
            title: error.Code,
            detail: error.Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code,
                ["correlationId"] = CorrelationContext.CorrelationId
            });
    }
}
