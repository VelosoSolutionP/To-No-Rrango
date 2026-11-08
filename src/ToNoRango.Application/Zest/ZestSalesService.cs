using System.Text.Json;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Delivery;
using ToNoRango.Application.Orders;
using ToNoRango.Application.Payments;
using ToNoRango.Domain.Delivery;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Orders;
using ToNoRango.Domain.Zest;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Zest;

public sealed record ZestSaleReply(string Text, string? ImageBase64 = null, string? FollowUp = null);

public sealed record ZestSaleResult(SalesState State, ZestSaleReply Reply);

public sealed record ZestSimulateRequest(SalesState? State, string Message, string? CustomerName);

public sealed record ZestSimulateResponse(SalesState State, string Reply, bool OrderWouldBeCreated);

public sealed record ZestStatsDto(int Conversations, int OrdersByZest, long RevenueByZestInCents, int KnowledgeEntries, int ActiveSales);

/// <summary>Connects the sales conversation to the real menu, delivery rules, orders and Pix.</summary>
public sealed class ZestSalesService(
    IMenuRepository menu,
    IEstablishmentRepository establishments,
    IRepository<Order> orders,
    IRepository<Conversation> conversations,
    IRepository<KnowledgeEntry> knowledge,
    DeliveryService delivery,
    OrderService orderService,
    PaymentService payments,
    ILookupClient lookups,
    IModuleAccess modules,
    IAppUrls urls,
    ZestService zest,
    IClock clock)
{
    private static readonly TimeSpan SaleTimeout = TimeSpan.FromHours(3);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(SalesState state) => JsonSerializer.Serialize(state, Json);

    public SalesState Restore(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SalesState();
        }

        var state = JsonSerializer.Deserialize<SalesState>(json, Json) ?? new SalesState();

        return clock.UtcNow - state.UpdatedAt > SaleTimeout ? new SalesState() : state;
    }

    public async Task<bool> CanSellAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await modules.IsAllowedAsync(tenantId, Modules.Pedidos, cancellationToken)
        && await modules.IsAllowedAsync(tenantId, Modules.Ia, cancellationToken);

    /// <summary>Handles the message when a sale is open or starting; returns null to let ZEST answer it as a question.</summary>
    public async Task<ZestSaleResult?> HandleAsync(
        Establishment establishment,
        SalesState state,
        string phone,
        string message,
        string? customerName,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var turn = await RunTurnAsync(establishment, state, message, customerName, cancellationToken);

        if (turn is null)
        {
            return null;
        }

        turn.State.UpdatedAt = clock.UtcNow;

        if (turn.Checkout)
        {
            return await PlaceOrderAsync(establishment, turn.State, phone, idempotencyKey, cancellationToken);
        }

        return new ZestSaleResult(turn.State, new ZestSaleReply(await WithQuestionAnswerAsync(establishment, phone, message, turn, cancellationToken)));
    }

    public async Task<Result<ZestSimulateResponse>> SimulateAsync(Guid establishmentId, ZestSimulateRequest request, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<ZestSimulateResponse>.Failure(CommonErrors.EstablishmentNotFound);
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Result<ZestSimulateResponse>.Failure(AppErrors.Validation("Escreva uma mensagem."));
        }

        var state = request.State ?? new SalesState();
        var turn = await RunTurnAsync(establishment, state, request.Message, request.CustomerName, cancellationToken);

        if (turn is null)
        {
            var answer = await zest.AnswerAsync(establishment, null, request.Message, cancellationToken);
            return new ZestSimulateResponse(state, answer.Text, false);
        }

        if (turn.Checkout)
        {
            return new ZestSimulateResponse(
                new SalesState(),
                "Pedido confirmado! Nesta simulação nenhum pedido foi criado. No WhatsApp real, aqui o cliente recebe o número do pedido, o link de acompanhamento e o Pix.",
                true);
        }

        return new ZestSimulateResponse(turn.State, await WithQuestionAnswerAsync(establishment, null, request.Message, turn, cancellationToken), false);
    }

    public async Task<Result<ZestStatsDto>> GetStatsAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<ZestStatsDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        var since = clock.UtcNow.AddDays(-30);
        var sold = await orders.ListAsync(
            order => order.EstablishmentId == establishmentId && order.Channel == OrderChannel.WhatsApp && order.CreatedAt >= since && order.Status != OrderStatus.Cancelled,
            order => order.CreatedAt,
            cancellationToken,
            take: 20000);

        return new ZestStatsDto(
            await conversations.CountAsync(conversation => conversation.EstablishmentId == establishmentId && conversation.LastMessageAt >= since, cancellationToken),
            sold.Count,
            sold.Sum(order => order.TotalInCents),
            await knowledge.CountAsync(entry => entry.EstablishmentId == establishmentId && entry.IsActive, cancellationToken),
            await conversations.CountAsync(conversation => conversation.EstablishmentId == establishmentId && conversation.SalesState != null && conversation.LastMessageAt >= clock.UtcNow.Add(-SaleTimeout), cancellationToken));
    }

    private async Task<SalesTurn?> RunTurnAsync(Establishment establishment, SalesState state, string message, string? customerName, CancellationToken cancellationToken)
    {
        var catalog = await BuildCatalogAsync(establishment, cancellationToken);

        if (state.Step == SalesStep.Idle && !ZestSalesFlow.WantsToOrder(message, catalog))
        {
            return null;
        }

        var ports = new Lookups(establishment, delivery, lookups);
        var turn = await ZestSalesFlow.HandleAsync(state, message, catalog, ports, customerName, cancellationToken);

        if (!turn.Unrecognized)
        {
            return turn;
        }

        var option = await zest.ChooseOptionAsync(establishment.Id, ZestSalesFlow.Prompt(turn.State, catalog), message, cancellationToken);

        return option is null ? turn : await ZestSalesFlow.HandleAsync(turn.State, option, catalog, ports, customerName, cancellationToken);
    }

    /// <summary>A question asked in the middle of a sale is answered without losing the sale.</summary>
    private async Task<string> WithQuestionAnswerAsync(Establishment establishment, string? phone, string message, SalesTurn turn, CancellationToken cancellationToken)
    {
        if (!turn.Unrecognized)
        {
            return turn.Reply;
        }

        var answer = await zest.AnswerAsync(establishment, phone, message, cancellationToken, allowLlm: false);

        if (answer.Intent is ZestIntent.Unknown or ZestIntent.Greeting or ZestIntent.Thanks)
        {
            return turn.Reply;
        }

        var catalog = await BuildCatalogAsync(establishment, cancellationToken);

        return $"{answer.Text}\n\n{ZestSalesFlow.Prompt(turn.State, catalog)}";
    }

    private async Task<ZestSaleResult> PlaceOrderAsync(Establishment establishment, SalesState state, string phone, string idempotencyKey, CancellationToken cancellationToken)
    {
        var delivering = state.Fulfillment == "Delivery";
        var request = new CreateOrderRequest(
            state.CustomerName!,
            phone,
            delivering ? FulfillmentType.Delivery : FulfillmentType.Pickup,
            state.Payment switch { "Pix" => PaymentMethod.Pix, "Cash" => PaymentMethod.Cash, _ => PaymentMethod.CardOnDelivery },
            state.Notes,
            idempotencyKey,
            delivering
                ? new DeliveryAddressRequest(state.Street!, state.Number!, state.Complement, state.District!, state.City!, state.State!, state.PostalCode!)
                : null,
            state.Cart.Select(line => new CreateOrderItemRequest(line.ItemId, line.Quantity, null, line.OptionIds)).ToList());

        var created = await orderService.CreateAsync(establishment.Slug, request, cancellationToken, OrderChannel.WhatsApp);

        if (created.IsFailure)
        {
            state.Step = SalesStep.CartReview;
            return new ZestSaleResult(state, new ZestSaleReply(
                $"Não consegui fechar o pedido: {created.Error.Description}\n\n{ZestSalesFlow.CartText(state)}\n\nPara tirar um item escreva *remover 1*, ou *atendente* para falar com a equipe."));
        }

        var order = created.Value;
        var confirmation = $"Pedido *{order.Code}* recebido! Total: {ZestSalesFlow.Money(order.TotalInCents)}.\nAcompanhe: {urls.TrackingUrl(order.Code)}";

        if (order.PaymentMethod != PaymentMethod.Pix)
        {
            return new ZestSaleResult(new SalesState(), new ZestSaleReply(confirmation));
        }

        var pix = await payments.CreatePixAsync(order.Code, new CreatePixRequest(null), cancellationToken);

        return pix.IsSuccess && pix.Value.PixQrCode is not null
            ? new ZestSaleResult(new SalesState(), new ZestSaleReply($"{confirmation}\n\nPague pelo QR Code ou pelo código abaixo. A confirmação é automática.", pix.Value.PixQrCodeBase64, pix.Value.PixQrCode))
            : new ZestSaleResult(new SalesState(), new ZestSaleReply($"{confirmation}\n\nO pagamento por Pix será combinado com o estabelecimento."));
    }

    private async Task<SalesCatalog> BuildCatalogAsync(Establishment establishment, CancellationToken cancellationToken)
    {
        var categories = await menu.ListCategoriesAsync(establishment.Id, false, cancellationToken);
        var items = await menu.ListItemsAsync(establishment.Id, false, cancellationToken);

        var sections = categories
            .OrderBy(category => category.SortOrder)
            .Select(category => new SalesCategory(
                category.Name,
                items.Where(item => item.CategoryId == category.Id)
                    .OrderBy(item => item.SortOrder)
                    .Select(item => new SalesItem(
                        item.Id,
                        item.Name,
                        item.Description,
                        item.PriceInCents,
                        item.IsAvailable,
                        item.OptionGroups.OrderBy(group => group.SortOrder)
                            .Select(group => new SalesGroup(
                                group.Name,
                                group.MinSelections,
                                group.MaxSelections,
                                group.Options.OrderBy(option => option.SortOrder)
                                    .Select(option => new SalesOption(option.Id, option.Name, option.PriceInCents, option.IsAvailable))
                                    .ToList()))
                            .ToList()))
                    .ToList()))
            .ToList();

        return new SalesCatalog(
            urls.MenuUrl(establishment.Slug),
            sections,
            establishment.IsDeliveryEnabled,
            establishment.IsPickupEnabled,
            establishment.MinimumOrderInCents,
            establishment.City,
            establishment.State,
            establishment.PostalCode);
    }

    private sealed class Lookups(Establishment establishment, DeliveryService delivery, ILookupClient client) : ISalesLookups
    {
        public Task<CepData?> FindCepAsync(string cep, CancellationToken cancellationToken) => client.FindCepAsync(cep, cancellationToken);

        public async Task<(long? FeeInCents, string? Error)> QuoteAsync(string district, CancellationToken cancellationToken)
        {
            var quote = await delivery.QuoteAsync(establishment, district, cancellationToken);
            return quote.IsSuccess ? (quote.Value.FeeInCents, null) : (null, quote.Error.Description);
        }
    }
}
