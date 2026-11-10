using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Contracts;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Application.Orders;

/// <summary>
/// Everything that must happen with a status or payment change. Callers run it inside one
/// transaction, so stock, finance and the outgoing notification commit together with the order.
/// </summary>
public sealed class OrderLifecycle(
    IRepository<RecipeLine> recipes,
    IRepository<StockItem> stockItems,
    IRepository<StockMovement> movements,
    IStockStore stock,
    IRepository<FinancialEntry> entries,
    OrderNotifier notifier,
    IClock clock)
{
    public async Task ChangeStatusAsync(
        Order order,
        OrderStatus target,
        string? changedBy,
        string? reason,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        order.ChangeStatus(target, changedBy, reason, now);

        if (target == OrderStatus.Confirmed)
        {
            await DeductStockAsync(order, changedBy, now, cancellationToken);
        }

        if (target == OrderStatus.Cancelled && order.StockDeductedAt is not null)
        {
            await RestoreStockAsync(order, changedBy, now, cancellationToken);
        }

        if (target == OrderStatus.Delivered
            && order.PaymentMethod is PaymentMethod.Cash or PaymentMethod.CardOnDelivery
            && order.PaymentStatus == PaymentStatus.Pending)
        {
            await MarkPaidAsync(order, cancellationToken);
        }

        await notifier.NotifyStatusAsync(order, cancellationToken);
    }

    public async Task MarkPaidAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return;
        }

        var now = clock.UtcNow;
        order.SetPaymentStatus(PaymentStatus.Paid, now);

        var alreadyPosted = await entries.AnyAsync(
            entry => entry.Source == FinancialSource.Order && entry.SourceId == order.Id,
            cancellationToken);

        if (alreadyPosted || order.TotalInCents <= 0)
        {
            return;
        }

        var entry = new FinancialEntry(
            order.TenantId,
            order.EstablishmentId,
            FinancialType.Receivable,
            FinancialSource.Order,
            order.Id,
            "Vendas",
            $"Pedido {order.Code}",
            order.TotalInCents,
            DateOnly.FromDateTime(now.UtcDateTime),
            now);
        entry.Settle(now);
        entries.Add(entry);
    }

    public async Task MarkRefundedAsync(Order order, CancellationToken cancellationToken)
    {
        order.SetPaymentStatus(PaymentStatus.Refunded, clock.UtcNow);

        var entry = await entries.FirstOrDefaultAsync(
            item => item.Source == FinancialSource.Order && item.SourceId == order.Id,
            cancellationToken);

        entry?.Cancel();
    }

    private async Task DeductStockAsync(Order order, string? changedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (order.StockDeductedAt is not null)
        {
            return;
        }

        var menuItemIds = order.Items.Select(item => item.MenuItemId).Distinct().ToList();
        var lines = await recipes.ListAsync(line => menuItemIds.Contains(line.MenuItemId), line => line.Id, cancellationToken, take: 2000);

        var totals = order.Items
            .SelectMany(item => lines
                .Where(line => line.MenuItemId == item.MenuItemId)
                .Select(line => (line.StockItemId, Quantity: line.Quantity * item.Quantity)))
            .GroupBy(entry => entry.StockItemId)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Quantity));

        if (totals.Count == 0)
        {
            return;
        }

        var stockIds = totals.Keys.ToList();
        var costs = (await stockItems.ListAsync(item => stockIds.Contains(item.Id), item => item.Id, cancellationToken, take: 2000))
            .ToDictionary(item => item.Id, item => item.AverageCostInCents);

        foreach (var (stockItemId, quantity) in totals)
        {
            await stock.ApplyDeltaAsync(stockItemId, -quantity, now, cancellationToken);
            movements.Add(new StockMovement(
                order.TenantId,
                order.EstablishmentId,
                stockItemId,
                StockMovementType.SaleDeduction,
                -quantity,
                costs.GetValueOrDefault(stockItemId),
                order.Id,
                null,
                $"Pedido {order.Code}",
                changedBy,
                now));
        }

        order.MarkStockDeducted(now);
    }

    private async Task RestoreStockAsync(Order order, string? changedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var deductions = await movements.ListAsync(
            movement => movement.OrderId == order.Id && movement.Type == StockMovementType.SaleDeduction,
            movement => movement.Id,
            cancellationToken,
            take: 2000);

        foreach (var deduction in deductions)
        {
            await stock.ApplyDeltaAsync(deduction.StockItemId, -deduction.Quantity, now, cancellationToken);
            movements.Add(new StockMovement(
                order.TenantId,
                order.EstablishmentId,
                deduction.StockItemId,
                StockMovementType.SaleReversal,
                -deduction.Quantity,
                deduction.UnitCostInCents,
                order.Id,
                null,
                $"Cancelamento do pedido {order.Code}",
                changedBy,
                now));
        }

        order.MarkStockRestored();
    }
}

public sealed class OrderNotifier(
    IRepository<WhatsAppSettings> settings,
    IRepository<OutboundMessage> outbox,
    IWhatsAppSender sender,
    IModuleAccess modules,
    IAppUrls urls,
    IClock clock)
{
    public async Task NotifyStatusAsync(Order order, CancellationToken cancellationToken)
    {
        // Counter sales without a phone have nobody to tell.
        if (!sender.IsConfigured || Domain.Common.PhoneNumber.Digits(order.CustomerPhone).Length < 10)
        {
            return;
        }

        var current = await settings.FirstOrDefaultAsync(item => item.EstablishmentId == order.EstablishmentId, cancellationToken);

        if (current is not { IsReady: true, NotifyOrderStatus: true }
            || !await modules.IsAllowedAsync(order.TenantId, Modules.WhatsApp, cancellationToken))
        {
            return;
        }

        outbox.Add(new OutboundMessage(
            order.TenantId,
            order.EstablishmentId,
            order.CustomerPhone,
            OrderMessages.For(order, urls.TrackingUrl(order.Code)),
            clock.UtcNow));
    }
}

public static class OrderMessages
{
    public static string StatusLabel(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "recebido, aguardando confirmação",
        OrderStatus.Confirmed => "confirmado",
        OrderStatus.Preparing => "em preparo",
        OrderStatus.Ready => "pronto",
        OrderStatus.OutForDelivery => "saiu para entrega",
        OrderStatus.Delivered => "entregue",
        OrderStatus.Cancelled => "cancelado",
        _ => "em andamento"
    };

    public static string For(Order order, string trackingUrl)
    {
        var headline = order.Status switch
        {
            OrderStatus.Pending => $"Recebemos seu pedido {order.Code}!",
            OrderStatus.Confirmed => $"Seu pedido {order.Code} foi confirmado.",
            OrderStatus.Preparing => $"Seu pedido {order.Code} já está em preparo.",
            OrderStatus.Ready when order.FulfillmentType == FulfillmentType.Pickup => $"Seu pedido {order.Code} está pronto para retirada.",
            OrderStatus.Ready => $"Seu pedido {order.Code} está pronto.",
            OrderStatus.OutForDelivery => $"Seu pedido {order.Code} saiu para entrega.",
            OrderStatus.Delivered => $"Pedido {order.Code} entregue. Bom apetite!",
            OrderStatus.Cancelled => $"Seu pedido {order.Code} foi cancelado. Motivo: {order.CancelReason}",
            _ => $"Seu pedido {order.Code} foi atualizado."
        };

        return $"{headline}\nAcompanhe: {trackingUrl}";
    }
}
