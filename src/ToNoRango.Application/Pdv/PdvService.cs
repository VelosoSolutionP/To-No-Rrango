using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Orders;
using ToNoRango.Domain.Orders;
using ToNoRango.Domain.Pdv;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Pdv;

public sealed record OpenCashRequest(long OpeningAmountInCents);

public sealed record CashMovementRequest(long AmountInCents, string Reason);

public sealed record CloseCashRequest(long CountedCashInCents, string? Notes);

public sealed record CounterSaleRequest(
    string? CustomerName,
    string? CustomerPhone,
    FulfillmentType FulfillmentType,
    PaymentMethod PaymentMethod,
    string? TableLabel,
    string? Notes,
    long DiscountInCents,
    string? DiscountReason,
    DeliveryAddressRequest? DeliveryAddress,
    IReadOnlyList<CreateOrderItemRequest> Items,
    string? IdempotencyKey = null);

public sealed record ReceiveOrderRequest(PaymentMethod PaymentMethod);

public sealed record CancelSaleRequest(string Reason);

public sealed record CashMovementDto(
    Guid Id,
    CashMovementType Type,
    PaymentMethod Method,
    long AmountInCents,
    string Reason,
    string OperatorName,
    Guid? OrderId,
    DateTimeOffset CreatedAt)
{
    public static CashMovementDto From(CashMovement movement) => new(
        movement.Id,
        movement.Type,
        movement.Method,
        movement.AmountInCents,
        movement.Reason,
        movement.OperatorName,
        movement.OrderId,
        movement.CreatedAt);
}

/// <summary>Net received per payment method in the shift (sales minus reversals).</summary>
public sealed record MethodTotalDto(PaymentMethod Method, long AmountInCents, int Sales);

public sealed record CashSessionDto(
    Guid Id,
    CashSessionStatus Status,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long OpeningAmountInCents,
    long ExpectedCashInCents,
    long SuppliesInCents,
    long WithdrawalsInCents,
    IReadOnlyList<MethodTotalDto> Totals,
    string? ClosedBy,
    DateTimeOffset? ClosedAt,
    long? CountedCashInCents,
    long? DifferenceInCents,
    string? ClosingNotes,
    IReadOnlyList<CashMovementDto> Movements)
{
    public static CashSessionDto From(CashSession session) => new(
        session.Id,
        session.Status,
        session.OpenedBy,
        session.OpenedAt,
        session.OpeningAmountInCents,
        session.ExpectedCashInCents ?? session.ExpectedCash,
        session.Movements.Where(movement => movement.Type == CashMovementType.Supply).Sum(movement => movement.AmountInCents),
        session.Movements.Where(movement => movement.Type == CashMovementType.Withdrawal).Sum(movement => movement.AmountInCents),
        session.Movements
            .Where(movement => movement.Type is CashMovementType.Sale or CashMovementType.SaleReversal)
            .GroupBy(movement => movement.Method)
            .Select(group => new MethodTotalDto(
                group.Key,
                group.Sum(movement => movement.Type == CashMovementType.Sale ? movement.AmountInCents : -movement.AmountInCents),
                group.Count(movement => movement.Type == CashMovementType.Sale) - group.Count(movement => movement.Type == CashMovementType.SaleReversal)))
            .OrderBy(total => total.Method)
            .ToList(),
        session.ClosedBy,
        session.ClosedAt,
        session.CountedCashInCents,
        session.DifferenceInCents,
        session.ClosingNotes,
        session.Movements.OrderByDescending(movement => movement.CreatedAt).Select(CashMovementDto.From).ToList());
}

public sealed record PdvOverviewDto(CashSessionDto? Current, IReadOnlyList<CashSessionDto> History);

public sealed record CounterSaleDto(OrderDto Order, CashSessionDto Session);

/// <summary>
/// The register: one open shift per store, with supplies, withdrawals and every sale received
/// there. Counter sales become regular orders, so kitchen, stock and finance follow them.
/// </summary>
public sealed class PdvService(
    IEstablishmentRepository establishments,
    IOrderRepository orders,
    IRepository<CashSession> sessions,
    OrderService orderService,
    OrderLifecycle lifecycle,
    IOrderEvents events,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const string CounterCustomer = "Cliente balcão";

    public async Task<Result<PdvOverviewDto>> GetAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var current = await FindOpenAsync(establishmentId, cancellationToken);
        var history = await sessions.ListAsync(
            session => session.EstablishmentId == establishmentId && session.Status == CashSessionStatus.Closed,
            session => session.OpenedAt,
            cancellationToken,
            descending: true,
            take: 20);

        return new PdvOverviewDto(current is null ? null : CashSessionDto.From(current), history.Select(CashSessionDto.From).ToList());
    }

    public Task<Result<CashSessionDto>> OpenAsync(Guid establishmentId, OpenCashRequest request, string operatorName, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CashSessionDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<CashSessionDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            if (await FindOpenAsync(establishmentId, cancellationToken) is not null)
            {
                return Result<CashSessionDto>.Failure(AppErrors.Conflict("pdv.already_open", "Já existe um caixa aberto. Feche-o antes de abrir outro."));
            }

            var session = new CashSession(establishment.TenantId, establishment.Id, operatorName, request.OpeningAmountInCents, clock.UtcNow);
            sessions.Add(session);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CashSessionDto.From(session);
        });

    public Task<Result<CashSessionDto>> SupplyAsync(Guid establishmentId, CashMovementRequest request, string operatorName, CancellationToken cancellationToken) =>
        ChangeOpenAsync(establishmentId, session => session.Supply(request.AmountInCents, request.Reason, operatorName, clock.UtcNow), cancellationToken);

    public Task<Result<CashSessionDto>> WithdrawAsync(Guid establishmentId, CashMovementRequest request, string operatorName, CancellationToken cancellationToken) =>
        ChangeOpenAsync(establishmentId, session => session.Withdraw(request.AmountInCents, request.Reason, operatorName, clock.UtcNow), cancellationToken);

    public Task<Result<CashSessionDto>> CloseAsync(Guid establishmentId, CloseCashRequest request, string operatorName, CancellationToken cancellationToken) =>
        ChangeOpenAsync(establishmentId, session => session.Close(request.CountedCashInCents, operatorName, request.Notes, clock.UtcNow), cancellationToken);

    /// <summary>
    /// Sells at the counter. Paid on the spot (cash, card or Pix in the machine), registered in the
    /// open shift and sent straight to the kitchen as confirmed.
    /// </summary>
    public async Task<Result<CounterSaleDto>> SellAsync(
        Guid establishmentId,
        CounterSaleRequest request,
        string operatorName,
        bool canDiscount,
        CancellationToken cancellationToken)
    {
        if (request.DiscountInCents > 0 && !canDiscount)
        {
            return Result<CounterSaleDto>.Failure(Error.Forbidden("pdv.discount", "Desconto só com gerente ou dono."));
        }

        if (request.PaymentMethod == PaymentMethod.Online)
        {
            return Result<CounterSaleDto>.Failure(AppErrors.Validation("No balcão, receba em dinheiro, cartão ou Pix."));
        }

        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<CounterSaleDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        var session = await FindOpenAsync(establishmentId, cancellationToken);

        if (session is null)
        {
            return Result<CounterSaleDto>.Failure(AppErrors.Validation("Abra o caixa antes de vender."));
        }

        var orderRequest = new CreateOrderRequest(
            string.IsNullOrWhiteSpace(request.CustomerName) ? CounterCustomer : request.CustomerName.Trim(),
            string.IsNullOrWhiteSpace(request.CustomerPhone) ? "balcao" : request.CustomerPhone.Trim(),
            request.FulfillmentType,
            request.PaymentMethod,
            request.Notes,
            request.IdempotencyKey,
            request.DeliveryAddress,
            request.Items);

        var created = await orderService.CreateAsync(
            establishment.Slug,
            orderRequest,
            cancellationToken,
            OrderChannel.Pdv,
            operatorName,
            order =>
            {
                order.SetTable(request.TableLabel);
                order.ApplyDiscount(request.DiscountInCents, request.DiscountReason, clock.UtcNow);
            },
            async (order, token) =>
            {
                await lifecycle.ChangeStatusAsync(order, OrderStatus.Confirmed, operatorName, "Venda no balcão.", token);
                await lifecycle.MarkPaidAsync(order, token);
                session.RegisterSale(order, operatorName, clock.UtcNow);
                await unitOfWork.SaveChangesAsync(token);
            });

        if (created.IsFailure)
        {
            return Result<CounterSaleDto>.Failure(created.Error);
        }

        var saved = await orders.GetByCodeAsync(created.Value.Code, cancellationToken);
        await events.PublishAsync(created.Value.Code);

        return new CounterSaleDto(OrderDto.From(saved!), CashSessionDto.From((await FindOpenAsync(establishmentId, cancellationToken))!));
    }

    /// <summary>Receives at the counter an order that came from the menu or WhatsApp to be paid on delivery or pickup.</summary>
    public Task<Result<CounterSaleDto>> ReceiveAsync(Guid establishmentId, Guid orderId, ReceiveOrderRequest request, string operatorName, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CounterSaleDto>(() => unitOfWork.ExecuteInTransactionAsync<Result<CounterSaleDto>>(async token =>
        {
            var session = await FindOpenAsync(establishmentId, token);

            if (session is null)
            {
                return Result<CounterSaleDto>.Failure(AppErrors.Validation("Abra o caixa antes de receber."));
            }

            var order = await orders.GetByIdAsync(orderId, token);

            if (order is null || order.EstablishmentId != establishmentId)
            {
                return Result<CounterSaleDto>.Failure(AppErrors.NotFound("order.not_found", "Pedido não encontrado."));
            }

            if (order.Status == OrderStatus.Cancelled || order.PaymentStatus != PaymentStatus.Pending)
            {
                return Result<CounterSaleDto>.Failure(AppErrors.Validation("Este pedido não tem valor a receber."));
            }

            if (request.PaymentMethod == PaymentMethod.Online)
            {
                return Result<CounterSaleDto>.Failure(AppErrors.Validation("No balcão, receba em dinheiro, cartão ou Pix."));
            }

            order.ChangePaymentMethod(request.PaymentMethod, clock.UtcNow);
            await lifecycle.MarkPaidAsync(order, token);
            session.RegisterSale(order, operatorName, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(token);

            return new CounterSaleDto(OrderDto.From(order), CashSessionDto.From(session));
        }, cancellationToken));

    /// <summary>Cancels a sale of the open shift: the order is cancelled (stock returns) and the money leaves the register.</summary>
    public async Task<Result<CounterSaleDto>> CancelSaleAsync(Guid establishmentId, Guid orderId, CancelSaleRequest request, string operatorName, CancellationToken cancellationToken)
    {
        var result = await Guarded.RunAsync<CounterSaleDto>(() => unitOfWork.ExecuteInTransactionAsync<Result<CounterSaleDto>>(async token =>
        {
            var session = await FindOpenAsync(establishmentId, token);

            if (session is null || session.Movements.All(movement => movement.OrderId != orderId))
            {
                return Result<CounterSaleDto>.Failure(AppErrors.Validation("Só dá para cancelar aqui vendas do caixa aberto."));
            }

            var order = await orders.GetByIdAsync(orderId, token);

            if (order is null)
            {
                return Result<CounterSaleDto>.Failure(AppErrors.NotFound("order.not_found", "Pedido não encontrado."));
            }

            if (order.CanTransitionTo(OrderStatus.Cancelled))
            {
                await lifecycle.ChangeStatusAsync(order, OrderStatus.Cancelled, operatorName, request.Reason, token);
            }
            else if (order.Status != OrderStatus.Cancelled)
            {
                return Result<CounterSaleDto>.Failure(AppErrors.Validation("Pedido já entregue: faça a devolução pelo financeiro."));
            }

            session.ReverseSale(order, operatorName, clock.UtcNow);
            await lifecycle.MarkRefundedAsync(order, token);
            await unitOfWork.SaveChangesAsync(token);

            return new CounterSaleDto(OrderDto.From(order), CashSessionDto.From(session));
        }, cancellationToken));

        if (result.IsSuccess)
        {
            await events.PublishAsync(result.Value.Order.Code);
        }

        return result;
    }

    private Task<CashSession?> FindOpenAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        sessions.FirstOrDefaultAsync(session => session.EstablishmentId == establishmentId && session.Status == CashSessionStatus.Open, cancellationToken);

    private Task<Result<CashSessionDto>> ChangeOpenAsync(Guid establishmentId, Action<CashSession> change, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CashSessionDto>(async () =>
        {
            var session = await FindOpenAsync(establishmentId, cancellationToken);

            if (session is null)
            {
                return Result<CashSessionDto>.Failure(AppErrors.Validation("Não há caixa aberto."));
            }

            change(session);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CashSessionDto.From(session);
        });
}
