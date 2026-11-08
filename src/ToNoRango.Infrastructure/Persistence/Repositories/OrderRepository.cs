using Microsoft.EntityFrameworkCore;
using ToNoRango.Application.Abstractions;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository(ToNoRangoDbContext dbContext) : IOrderRepository
{
    public Task<Order?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        Query().FirstOrDefaultAsync(order => order.Code == code, cancellationToken);

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        Query().FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);

    public Task<Order?> GetByIdempotencyKeyAsync(
        Guid establishmentId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        Query().FirstOrDefaultAsync(
            order => order.EstablishmentId == establishmentId && order.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<IReadOnlyList<Order>> ListAsync(
        Guid establishmentId,
        OrderStatus? status,
        int take,
        CancellationToken cancellationToken)
    {
        var query = Query()
            .AsNoTracking()
            .Where(order => order.EstablishmentId == establishmentId);

        if (status is not null)
        {
            query = query.Where(order => order.Status == status);
        }

        return await query
            .OrderByDescending(order => order.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        dbContext.Orders.Add(order);
        return Task.CompletedTask;
    }

    private IQueryable<Order> Query() =>
        dbContext.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Options)
            .Include(order => order.StatusHistory);
}
