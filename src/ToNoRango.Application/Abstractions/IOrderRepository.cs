using ToNoRango.Domain.Orders;

namespace ToNoRango.Application.Abstractions;

public interface IOrderRepository
{
    Task<Order?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task<Order?> GetByIdempotencyKeyAsync(Guid establishmentId, string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListAsync(Guid establishmentId, OrderStatus? status, int take, CancellationToken cancellationToken);

    Task AddAsync(Order order, CancellationToken cancellationToken);
}
