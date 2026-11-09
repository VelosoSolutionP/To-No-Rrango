using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Delivery;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Delivery;

public sealed record CourierRequest(string Name, string Phone, bool IsActive = true);

public sealed record CourierDto(Guid Id, string Name, string Phone, bool IsActive)
{
    public static CourierDto From(Courier courier) => new(courier.Id, courier.Name, courier.Phone, courier.IsActive);
}

public sealed class CourierService(
    IRepository<Courier> couriers,
    IEstablishmentRepository establishments,
    IUnitOfWork unitOfWork)
{
    private static readonly Error DuplicatePhone = AppErrors.Conflict("courier.duplicate_phone", "Já existe entregador com este WhatsApp.");

    public async Task<Result<IReadOnlyList<CourierDto>>> ListAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await couriers.ListAsync(courier => courier.EstablishmentId == establishmentId, courier => courier.Name, cancellationToken);
        return list.Select(CourierDto.From).ToList();
    }

    public Task<Result<CourierDto>> CreateAsync(Guid establishmentId, CourierRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CourierDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<CourierDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var phone = Courier.NormalizePhone(request.Phone);

            if (await couriers.AnyAsync(courier => courier.EstablishmentId == establishment.Id && courier.Phone == phone, cancellationToken))
            {
                return Result<CourierDto>.Failure(DuplicatePhone);
            }

            var courier = new Courier(establishment.TenantId, establishment.Id, request.Name, phone);
            couriers.Add(courier);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CourierDto.From(courier);
        });

    public Task<Result<CourierDto>> UpdateAsync(Guid courierId, CourierRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CourierDto>(async () =>
        {
            var courier = await couriers.FindAsync(courierId, cancellationToken);

            if (courier is null)
            {
                return Result<CourierDto>.Failure(AppErrors.NotFound("courier.not_found", "Entregador não encontrado."));
            }

            var phone = Courier.NormalizePhone(request.Phone);

            if (await couriers.AnyAsync(other => other.Id != courier.Id && other.EstablishmentId == courier.EstablishmentId && other.Phone == phone, cancellationToken))
            {
                return Result<CourierDto>.Failure(DuplicatePhone);
            }

            courier.Update(request.Name, phone, request.IsActive);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CourierDto.From(courier);
        });
}
