using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Common;

/// <summary>Turns domain guard failures and write conflicts into results the API can answer with.</summary>
internal static class Guarded
{
    public static async Task<Result<T>> RunAsync<T>(Func<Task<Result<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (PersistenceConflictException)
        {
            return Result<T>.Failure(AppErrors.Conflict(
                "persistence.conflict",
                "Este registro foi alterado ao mesmo tempo por outra operação. Tente novamente."));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<T>(exception);
        }
    }
}
