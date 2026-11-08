using Microsoft.EntityFrameworkCore;
using ToNoRango.Application.Abstractions;
using ToNoRango.Domain.Establishments;

namespace ToNoRango.Infrastructure.Persistence.Repositories;

public sealed class EstablishmentRepository(ToNoRangoDbContext dbContext) : IEstablishmentRepository
{
    public Task<Establishment?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Establishments.FirstOrDefaultAsync(establishment => establishment.Slug == slug, cancellationToken);

    public Task<Establishment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Establishments.FirstOrDefaultAsync(establishment => establishment.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Establishment>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Establishments
            .OrderBy(establishment => establishment.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        dbContext.Establishments.AnyAsync(
            establishment => establishment.Slug == slug && (exceptId == null || establishment.Id != exceptId),
            cancellationToken);

    public Task AddAsync(Establishment establishment, CancellationToken cancellationToken)
    {
        dbContext.Establishments.Add(establishment);
        return Task.CompletedTask;
    }
}
