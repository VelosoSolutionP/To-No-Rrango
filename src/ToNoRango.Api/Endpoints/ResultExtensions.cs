using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Api.Endpoints;

internal static class ResultExtensions
{
    public static IResult ToHttpResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();

    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();

    private static IResult ToProblem(this Error error) => error.Type switch
    {
        ErrorType.Validation => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["error"] = [error.Description] },
            title: error.Code),
        ErrorType.NotFound => Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodes.Status404NotFound),
        ErrorType.Conflict => Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodes.Status409Conflict),
        ErrorType.Unauthorized => Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodes.Status401Unauthorized),
        ErrorType.Forbidden => Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodes.Status403Forbidden),
        ErrorType.TooManyRequests => Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodes.Status429TooManyRequests),
        ErrorType.External => Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodes.Status400BadRequest)
    };
}
