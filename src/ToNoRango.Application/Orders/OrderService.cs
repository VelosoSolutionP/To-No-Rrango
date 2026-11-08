using System.Security.Cryptography;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Delivery;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Orders;

public sealed class OrderService(
    IEstablishmentRepository establishments,
    IMenuRepository menu,
    IOrderRepository orders,
    ICustomerStore customers,
    ContractService contracts,
    DeliveryService delivery,
    OrderLifecycle lifecycle,
    OrderNotifier notifier,
    IOrderEvents events,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<OrderTrackingDto>> CreateAsync(
        string slug,
        CreateOrderRequest request,
        CancellationToken cancellationToken = default,
        OrderChannel channel = OrderChannel.Web)
    {
        try
        {
            var establishment = await establishments.GetBySlugAsync(slug, cancellationToken);

            if (establishment is null || !establishment.IsActive)
            {
                return Result<OrderTrackingDto>.Failure(
                    AppErrors.NotFound("establishment.not_found", "Estabelecimento não encontrado."));
            }

            if (!await contracts.IsAllowedAsync(establishment.TenantId, Modules.Pedidos, cancellationToken))
            {
                return Result<OrderTrackingDto>.Failure(
                    Error.Forbidden("contract.blocked", "Este estabelecimento não está recebendo pedidos no momento."));
            }

            var orderLimitError = await contracts.CheckOrderLimitAsync(establishment.TenantId, cancellationToken);

            if (orderLimitError is not null)
            {
                return Result<OrderTrackingDto>.Failure(orderLimitError);
            }

            if (!IsFulfillmentEnabled(establishment, request.FulfillmentType))
            {
                return Result<OrderTrackingDto>.Failure(
                    AppErrors.Validation("Esta modalidade de atendimento não está disponível."));
            }

            if (request.Items is null || request.Items.Count == 0)
            {
                return Result<OrderTrackingDto>.Failure(
                    AppErrors.Validation("O pedido precisa ter pelo menos um item."));
            }

            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing = await orders.GetByIdempotencyKeyAsync(
                    establishment.Id,
                    request.IdempotencyKey.Trim(),
                    cancellationToken);

                if (existing is not null)
                {
                    return OrderTrackingDto.From(existing);
                }
            }

            var address = request.DeliveryAddress;
            var deliveryFee = 0L;

            if (request.FulfillmentType == FulfillmentType.Delivery)
            {
                if (string.IsNullOrWhiteSpace(address?.District))
                {
                    return Result<OrderTrackingDto>.Failure(AppErrors.Validation("Informe o bairro para entrega."));
                }

                var quote = await delivery.QuoteAsync(establishment, address.District, cancellationToken);

                if (quote.IsFailure)
                {
                    return Result<OrderTrackingDto>.Failure(quote.Error);
                }

                deliveryFee = quote.Value.FeeInCents;
            }

            var now = clock.UtcNow;
            var order = Order.Create(
                establishment.TenantId,
                establishment.Id,
                GenerateCode(now),
                request.CustomerName,
                request.CustomerPhone,
                request.FulfillmentType,
                request.PaymentMethod,
                address?.Street,
                address?.Number,
                address?.Complement,
                address?.District,
                address?.City,
                address?.State,
                address?.PostalCode,
                request.Notes,
                request.IdempotencyKey,
                deliveryFee,
                channel == OrderChannel.WhatsApp ? "zest" : "cliente",
                now,
                channel);

            foreach (var line in request.Items)
            {
                var item = await menu.GetItemAsync(line.MenuItemId, cancellationToken);

                if (item is null || item.EstablishmentId != establishment.Id || !item.IsActive)
                {
                    return Result<OrderTrackingDto>.Failure(
                        AppErrors.Validation("Um dos itens não está disponível neste estabelecimento."));
                }

                order.AddItem(item, line.Quantity, line.Notes, line.OptionIds);
            }

            if (order.SubtotalInCents < establishment.MinimumOrderInCents)
            {
                return Result<OrderTrackingDto>.Failure(
                    AppErrors.Validation(
                        $"O pedido mínimo é de R$ {establishment.MinimumOrderInCents / 100m:0.00}."));
            }

            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await orders.AddAsync(order, token);
                await unitOfWork.SaveChangesAsync(token);
                await customers.RegisterOrderAsync(order, token);

                // In a WhatsApp sale the conversation itself tells the customer the order was received.
                if (channel != OrderChannel.WhatsApp)
                {
                    await notifier.NotifyStatusAsync(order, token);
                }

                return true;
            }, cancellationToken);

            return OrderTrackingDto.From(order);
        }
        catch (PersistenceConflictException)
        {
            return Result<OrderTrackingDto>.Failure(
                AppErrors.Conflict("order.duplicate", "Este pedido já foi enviado. Confira o acompanhamento."));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return AppErrors.FromDomain<OrderTrackingDto>(exception);
        }
    }

    public async Task<Result<OrderTrackingDto>> TrackAsync(string code, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetByCodeAsync(code, cancellationToken);

        return order is null
            ? Result<OrderTrackingDto>.Failure(
                AppErrors.NotFound("order.not_found", "Pedido não encontrado."))
            : OrderTrackingDto.From(order);
    }

    public async Task<Result<IReadOnlyList<OrderDto>>> ListAsync(
        Guid establishmentId,
        OrderStatus? status,
        int take,
        CancellationToken cancellationToken = default)
    {
        var ordersList = await orders.ListAsync(establishmentId, status, Math.Clamp(take, 1, 200), cancellationToken);

        return ordersList.Select(OrderDto.From).ToList();
    }

    public async Task<Result<OrderDto>> UpdateStatusAsync(
        Guid orderId,
        UpdateOrderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Guarded.RunAsync<OrderDto>(() => unitOfWork.ExecuteInTransactionAsync<Result<OrderDto>>(async token =>
        {
            var order = await orders.GetByIdAsync(orderId, token);

            if (order is null)
            {
                return Result<OrderDto>.Failure(
                    AppErrors.NotFound("order.not_found", "Pedido não encontrado."));
            }

            await lifecycle.ChangeStatusAsync(order, request.Status, request.ChangedBy, request.Reason, token);

            return OrderDto.From(order);
        }, cancellationToken));

        if (result.IsSuccess)
        {
            await events.PublishAsync(result.Value.Code);
        }

        return result;
    }

    /// <summary>Records a payment taken outside the online gateway (cash, card machine, Pix direto).</summary>
    public async Task<Result<OrderDto>> MarkPaidAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await Guarded.RunAsync<OrderDto>(() => unitOfWork.ExecuteInTransactionAsync<Result<OrderDto>>(async token =>
        {
            var order = await orders.GetByIdAsync(orderId, token);

            if (order is null)
            {
                return Result<OrderDto>.Failure(
                    AppErrors.NotFound("order.not_found", "Pedido não encontrado."));
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                return Result<OrderDto>.Failure(AppErrors.Validation("Um pedido cancelado não pode ser marcado como pago."));
            }

            await lifecycle.MarkPaidAsync(order, token);

            return OrderDto.From(order);
        }, cancellationToken));

        if (result.IsSuccess)
        {
            await events.PublishAsync(result.Value.Code);
        }

        return result;
    }

    private static bool IsFulfillmentEnabled(Establishment establishment, FulfillmentType fulfillmentType) => fulfillmentType switch
    {
        FulfillmentType.Pickup => establishment.IsPickupEnabled,
        FulfillmentType.Delivery => establishment.IsDeliveryEnabled,
        FulfillmentType.DineIn => establishment.IsDineInEnabled,
        _ => false
    };

    private static string GenerateCode(DateTimeOffset now) =>
        $"TR-{now:yyyyMMdd}-{RandomNumberGenerator.GetHexString(8)}";
}
