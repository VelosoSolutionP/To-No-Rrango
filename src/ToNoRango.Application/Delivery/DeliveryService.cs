using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Delivery;
using ToNoRango.Domain.Establishments;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Delivery;

public sealed record DeliveryZoneRequest(string District, long FeeInCents, int? EstimatedMinutes, bool IsActive = true);

public sealed record DeliveryZoneDto(Guid Id, string District, long FeeInCents, int? EstimatedMinutes, bool IsActive)
{
    public static DeliveryZoneDto From(DeliveryZone zone) => new(zone.Id, zone.District, zone.FeeInCents, zone.EstimatedMinutes, zone.IsActive);
}

public sealed record DeliveryQuoteDto(long FeeInCents, int? EstimatedMinutes);

/// <summary>Without active zones the establishment's flat fee applies; with zones only those districts are served.</summary>
public sealed class DeliveryService(
    IRepository<DeliveryZone> zones,
    IEstablishmentRepository establishments,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<DeliveryQuoteDto>> QuoteAsync(Establishment establishment, string district, CancellationToken cancellationToken)
    {
        var active = await zones.ListAsync(
            zone => zone.EstablishmentId == establishment.Id && zone.IsActive,
            zone => zone.District,
            cancellationToken);

        if (active.Count == 0)
        {
            return new DeliveryQuoteDto(establishment.DeliveryFeeInCents, null);
        }

        var key = string.IsNullOrWhiteSpace(district) ? string.Empty : DeliveryZone.KeyOf(district);
        var match = active.FirstOrDefault(zone => zone.DistrictKey == key);

        return match is null
            ? Result<DeliveryQuoteDto>.Failure(AppErrors.Validation("Ainda não entregamos neste bairro."))
            : new DeliveryQuoteDto(match.FeeInCents, match.EstimatedMinutes);
    }

    public async Task<Result<DeliveryQuoteDto>> QuoteBySlugAsync(string slug, string district, CancellationToken cancellationToken) =>
        await Guarded.RunAsync<DeliveryQuoteDto>(async () =>
        {
            var establishment = await establishments.GetBySlugAsync(slug, cancellationToken);

            return establishment is null
                ? Result<DeliveryQuoteDto>.Failure(CommonErrors.EstablishmentNotFound)
                : await QuoteAsync(establishment, district, cancellationToken);
        });

    public async Task<Result<IReadOnlyList<DeliveryZoneDto>>> ListAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await zones.ListAsync(zone => zone.EstablishmentId == establishmentId, zone => zone.District, cancellationToken);
        return list.Select(DeliveryZoneDto.From).ToList();
    }

    public Task<Result<DeliveryZoneDto>> CreateAsync(Guid establishmentId, DeliveryZoneRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<DeliveryZoneDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<DeliveryZoneDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var zone = new DeliveryZone(establishment.TenantId, establishment.Id, request.District, request.FeeInCents, request.EstimatedMinutes);
            zones.Add(zone);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return DeliveryZoneDto.From(zone);
        });

    public Task<Result<DeliveryZoneDto>> UpdateAsync(Guid zoneId, DeliveryZoneRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<DeliveryZoneDto>(async () =>
        {
            var zone = await zones.FindAsync(zoneId, cancellationToken);

            if (zone is null)
            {
                return Result<DeliveryZoneDto>.Failure(AppErrors.NotFound("zone.not_found", "Região não encontrada."));
            }

            zone.Update(request.District, request.FeeInCents, request.EstimatedMinutes, request.IsActive);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return DeliveryZoneDto.From(zone);
        });
}
