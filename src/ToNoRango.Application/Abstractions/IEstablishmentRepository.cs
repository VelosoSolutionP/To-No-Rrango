using ToNoRango.Domain.Establishments;

namespace ToNoRango.Application.Abstractions;

public interface IEstablishmentRepository
{
    Task<Establishment?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<Establishment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Establishment>> ListAsync(CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    Task AddAsync(Establishment establishment, CancellationToken cancellationToken);
}
