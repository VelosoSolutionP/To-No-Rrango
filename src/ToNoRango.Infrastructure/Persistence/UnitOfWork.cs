using Microsoft.EntityFrameworkCore;
using Npgsql;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Infrastructure.Persistence;

/// <summary>
/// One transaction per unit of work. Commands that must commit even when they return a failure
/// (failed sign in attempts advancing lockout, replayed refresh token revocation) opt in explicitly.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ToNoRangoDbContext _dbContext;

    public UnitOfWork(ToNoRangoDbContext dbContext) => _dbContext = dbContext;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConflictException("The row was changed by another operation.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PersistenceConflictException("A unique constraint rejected the change.", exception);
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var dbContext = _dbContext;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
