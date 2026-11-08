using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Common;

internal static class AppErrors
{
    public static Error Validation(string message) => Error.Validation("validation", message);

    public static Error NotFound(string code, string message) => Error.NotFound(code, message);

    public static Error Conflict(string code, string message) => Error.Conflict(code, message);

    public static Result<T> FromDomain<T>(Exception exception) =>
        Result<T>.Failure(Validation(exception.Message));
}

internal static class CommonErrors
{
    public static readonly Error EstablishmentNotFound =
        Error.NotFound("establishment.not_found", "Estabelecimento não encontrado.");
}
