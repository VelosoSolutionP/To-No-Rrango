namespace ToNoRango.SharedKernel.Abstractions;

/// <summary>One transaction per request, owned by the pipeline rather than by each handler.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
