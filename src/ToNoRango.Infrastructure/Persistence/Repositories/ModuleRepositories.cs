using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ToNoRango.Application.Abstractions;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Orders;
using ToNoRango.Domain.Parties;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Infrastructure.Persistence.Repositories;

public sealed class EfRepository<T>(ToNoRangoDbContext dbContext) : IRepository<T>
    where T : class
{
    private DbSet<T> Set => dbContext.Set<T>();

    public Task<T?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(entity => EF.Property<Guid>(entity, "Id") == id, cancellationToken);

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(predicate, cancellationToken);

    public async Task<IReadOnlyList<T>> ListAsync<TKey>(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, TKey>> orderBy,
        CancellationToken cancellationToken,
        bool descending = false,
        int take = 500)
    {
        var query = Set.AsNoTracking().Where(predicate);
        query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);

        return await query.Take(take).ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) =>
        Set.CountAsync(predicate, cancellationToken);

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) =>
        Set.AnyAsync(predicate, cancellationToken);

    public void Add(T entity) => Set.Add(entity);

    public void Remove(T entity) => Set.Remove(entity);
}

public sealed class StockStore(ToNoRangoDbContext dbContext) : IStockStore
{
    public Task ApplyDeltaAsync(Guid stockItemId, decimal delta, DateTimeOffset now, CancellationToken cancellationToken) =>
        dbContext.Set<StockItem>()
            .Where(item => item.Id == stockItemId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.QuantityOnHand, item => item.QuantityOnHand + delta)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);
}

public sealed class CustomerStore(ToNoRangoDbContext dbContext) : ICustomerStore
{
    public Task RegisterOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var id = GuidV7.New();
        var now = order.CreatedAt;

        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO parties.customers
                ("Id", "TenantId", "EstablishmentId", "Name", "Phone", "PhoneKey", "OrdersCount", "TotalOrderedInCents", "LastOrderAt", "CreatedAt", "UpdatedAt")
            VALUES
                ({id}, {order.TenantId}, {order.EstablishmentId}, {order.CustomerName}, {order.CustomerPhone}, {order.CustomerPhoneKey}, 1, {order.TotalInCents}, {now}, {now}, {now})
            ON CONFLICT ("EstablishmentId", "PhoneKey") DO UPDATE SET
                "Name" = EXCLUDED."Name",
                "Phone" = EXCLUDED."Phone",
                "OrdersCount" = customers."OrdersCount" + 1,
                "TotalOrderedInCents" = customers."TotalOrderedInCents" + EXCLUDED."TotalOrderedInCents",
                "LastOrderAt" = EXCLUDED."LastOrderAt",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, cancellationToken);
    }
}

public sealed class ReportQueries(ToNoRangoDbContext dbContext) : IReportQueries
{
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    public async Task<DashboardData> GetDashboardAsync(
        Guid establishmentId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var orders = dbContext.Orders.AsNoTracking()
            .Where(order => order.EstablishmentId == establishmentId && order.CreatedAt >= from && order.CreatedAt <= to);
        var valid = orders.Where(order => order.Status != OrderStatus.Cancelled);

        var byStatus = await orders
            .GroupBy(order => order.Status)
            .Select(group => new DashboardStatusCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var byPayment = await valid
            .GroupBy(order => order.PaymentMethod)
            .Select(group => new DashboardPaymentTotal(group.Key, group.Count(), group.Sum(order => order.TotalInCents)))
            .ToListAsync(cancellationToken);

        var paid = await valid
            .Where(order => order.PaymentStatus == PaymentStatus.Paid)
            .SumAsync(order => (long?)order.TotalInCents, cancellationToken) ?? 0;

        var validIds = valid.Select(order => order.Id);

        var topItems = await dbContext.OrderItems.AsNoTracking()
            .Where(item => validIds.Contains(item.OrderId))
            .GroupBy(item => item.Name)
            .Select(group => new { Name = group.Key, Quantity = group.Sum(item => item.Quantity), Revenue = group.Sum(item => item.TotalInCents) })
            .OrderByDescending(item => item.Quantity)
            .Take(10)
            .ToListAsync(cancellationToken);

        var points = await valid
            .Select(order => new { order.CreatedAt, order.TotalInCents })
            .ToListAsync(cancellationToken);

        var byDay = points
            .GroupBy(point => DateOnly.FromDateTime(point.CreatedAt.ToOffset(BrazilOffset).DateTime))
            .OrderBy(group => group.Key)
            .Select(group => new DashboardDay(group.Key, group.Count(), group.Sum(point => point.TotalInCents)))
            .ToList();

        return new DashboardData(
            byStatus.Sum(status => status.Count),
            byStatus.Where(status => status.Status == OrderStatus.Cancelled).Sum(status => status.Count),
            byPayment.Sum(payment => payment.TotalInCents),
            paid,
            byStatus,
            byPayment,
            topItems.Select(item => new DashboardTopItem(item.Name, item.Quantity, item.Revenue)).ToList(),
            byDay);
    }
}
