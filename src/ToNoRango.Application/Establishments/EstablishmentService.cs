using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Establishments;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Establishments;

public sealed class EstablishmentService(
    IEstablishmentRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<EstablishmentDto>> CreateAsync(
        CreateEstablishmentRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var slug = Establishment.NormalizeSlug(request.Slug);

            if (await repository.SlugExistsAsync(slug, null, cancellationToken))
            {
                return Result<EstablishmentDto>.Failure(
                    AppErrors.Conflict("establishment.slug_taken", "Este endereço já está em uso."));
            }

            var now = clock.UtcNow;
            var establishment = new Establishment(
                request.TenantId,
                request.Name,
                slug,
                now,
                request.Description,
                request.Phone,
                request.WhatsApp,
                request.AddressLine,
                request.City,
                request.State,
                request.PostalCode,
                request.LogoUrl,
                request.CoverUrl);

            establishment.SetBranding(request.PrimaryColor, request.SecondaryColor, request.LogoUrl, request.CoverUrl, now);
            establishment.SetFulfillment(
                request.IsPickupEnabled,
                request.IsDeliveryEnabled,
                request.IsDineInEnabled,
                request.DeliveryFeeInCents,
                request.MinimumOrderInCents,
                now);

            await repository.AddAsync(establishment, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return EstablishmentDto.From(establishment);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<EstablishmentDto>(exception);
        }
    }

    public async Task<Result<EstablishmentDto>> UpdateAsync(
        Guid id,
        UpdateEstablishmentRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var establishment = await repository.GetByIdAsync(id, cancellationToken);

            if (establishment is null)
            {
                return Result<EstablishmentDto>.Failure(
                    AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."));
            }

            var now = clock.UtcNow;
            establishment.UpdateProfile(
                request.Name,
                request.Description,
                request.Phone,
                request.WhatsApp,
                request.AddressLine,
                request.City,
                request.State,
                request.PostalCode,
                request.TimeZone,
                now);

            establishment.SetBranding(request.PrimaryColor, request.SecondaryColor, request.LogoUrl ?? establishment.LogoUrl, request.CoverUrl ?? establishment.CoverUrl, now);
            establishment.SetFulfillment(
                request.IsPickupEnabled,
                request.IsDeliveryEnabled,
                request.IsDineInEnabled,
                request.DeliveryFeeInCents,
                request.MinimumOrderInCents,
                now);

            if (request.IsActive)
            {
                establishment.Activate(now);
            }
            else
            {
                establishment.Deactivate(now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return EstablishmentDto.From(establishment);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<EstablishmentDto>(exception);
        }
    }

    public async Task<Result<IReadOnlyList<EstablishmentDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var establishments = await repository.ListAsync(cancellationToken);

        return establishments.Select(EstablishmentDto.From).ToList();
    }

    public async Task<Result<EstablishmentDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var establishment = await repository.GetBySlugAsync(slug, cancellationToken);

        return establishment is null
            ? Result<EstablishmentDto>.Failure(
                AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."))
            : EstablishmentDto.From(establishment);
    }

    public async Task<Result<EstablishmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var establishment = await repository.GetByIdAsync(id, cancellationToken);

        return establishment is null
            ? Result<EstablishmentDto>.Failure(
                AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."))
            : EstablishmentDto.From(establishment);
    }
}
