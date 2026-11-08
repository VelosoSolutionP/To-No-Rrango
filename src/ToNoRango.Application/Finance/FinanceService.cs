using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Inventory;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Finance;

public sealed record FinancialEntryRequest(FinancialType Type, string Category, string Description, long AmountInCents, DateOnly DueOn);

public sealed record FinancialEntryDto(
    Guid Id,
    FinancialType Type,
    FinancialStatus Status,
    FinancialSource Source,
    string Category,
    string Description,
    long AmountInCents,
    DateOnly DueOn,
    DateTimeOffset? SettledAt)
{
    public static FinancialEntryDto From(FinancialEntry entry) => new(
        entry.Id,
        entry.Type,
        entry.Status,
        entry.Source,
        entry.Category,
        entry.Description,
        entry.AmountInCents,
        entry.DueOn,
        entry.SettledAt);
}

public sealed record DashboardDto(
    DashboardData Sales,
    long ReceivedInCents,
    long PaidOutInCents,
    long OpenPayablesInCents,
    long OpenReceivablesInCents,
    long CostOfGoodsInCents,
    int LowStockCount);

public sealed class FinanceService(
    IRepository<FinancialEntry> entries,
    IRepository<StockItem> stockItems,
    IRepository<StockMovement> movements,
    IEstablishmentRepository establishments,
    IReportQueries reports,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<IReadOnlyList<FinancialEntryDto>>> ListAsync(Guid establishmentId, FinancialStatus? status, CancellationToken cancellationToken)
    {
        var list = await entries.ListAsync(
            entry => entry.EstablishmentId == establishmentId && (status == null || entry.Status == status),
            entry => entry.DueOn,
            cancellationToken,
            descending: true,
            take: 300);

        return list.Select(FinancialEntryDto.From).ToList();
    }

    public Task<Result<FinancialEntryDto>> CreateAsync(Guid establishmentId, FinancialEntryRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FinancialEntryDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<FinancialEntryDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var entry = new FinancialEntry(
                establishment.TenantId,
                establishment.Id,
                request.Type,
                FinancialSource.Manual,
                null,
                request.Category,
                request.Description,
                request.AmountInCents,
                request.DueOn,
                clock.UtcNow);

            entries.Add(entry);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return FinancialEntryDto.From(entry);
        });

    public Task<Result<FinancialEntryDto>> SettleAsync(Guid entryId, CancellationToken cancellationToken) =>
        ChangeAsync(entryId, entry => entry.Settle(clock.UtcNow), cancellationToken);

    public Task<Result<FinancialEntryDto>> CancelAsync(Guid entryId, CancellationToken cancellationToken) =>
        ChangeAsync(entryId, entry =>
        {
            if (entry.Source != FinancialSource.Manual)
            {
                throw new InvalidOperationException("Lançamentos automáticos são ajustados pelo pedido ou pela compra de origem.");
            }

            entry.Cancel();
        }, cancellationToken);

    public async Task<Result<DashboardDto>> GetDashboardAsync(Guid establishmentId, int days, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<DashboardDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        var to = clock.UtcNow;
        var from = to.AddDays(-Math.Clamp(days, 1, 366));
        var sales = await reports.GetDashboardAsync(establishmentId, from, to, cancellationToken);

        var financial = await entries.ListAsync(
            entry => entry.EstablishmentId == establishmentId && entry.Status != FinancialStatus.Cancelled,
            entry => entry.DueOn,
            cancellationToken,
            descending: true,
            take: 20000);

        var inPeriod = financial.Where(entry => entry.Status == FinancialStatus.Settled && entry.SettledAt >= from).ToList();

        var sold = await movements.ListAsync(
            movement => movement.EstablishmentId == establishmentId
                && (movement.Type == StockMovementType.SaleDeduction || movement.Type == StockMovementType.SaleReversal)
                && movement.CreatedAt >= from,
            movement => movement.CreatedAt,
            cancellationToken,
            take: 50000);

        var stock = await stockItems.ListAsync(item => item.EstablishmentId == establishmentId && item.IsActive, item => item.Name, cancellationToken, take: 5000);

        return new DashboardDto(
            sales,
            inPeriod.Where(entry => entry.Type == FinancialType.Receivable).Sum(entry => entry.AmountInCents),
            inPeriod.Where(entry => entry.Type == FinancialType.Payable).Sum(entry => entry.AmountInCents),
            financial.Where(entry => entry is { Status: FinancialStatus.Open, Type: FinancialType.Payable }).Sum(entry => entry.AmountInCents),
            financial.Where(entry => entry is { Status: FinancialStatus.Open, Type: FinancialType.Receivable }).Sum(entry => entry.AmountInCents),
            (long)Math.Round(sold.Sum(movement => -movement.Quantity * movement.UnitCostInCents), MidpointRounding.AwayFromZero),
            stock.Count(item => item.IsLow));
    }

    private Task<Result<FinancialEntryDto>> ChangeAsync(Guid entryId, Action<FinancialEntry> change, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FinancialEntryDto>(async () =>
        {
            var entry = await entries.FindAsync(entryId, cancellationToken);

            if (entry is null)
            {
                return Result<FinancialEntryDto>.Failure(AppErrors.NotFound("finance.not_found", "Lançamento não encontrado."));
            }

            change(entry);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return FinancialEntryDto.From(entry);
        });
}
