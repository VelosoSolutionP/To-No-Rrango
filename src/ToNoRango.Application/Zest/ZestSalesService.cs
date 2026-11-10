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
        var turn = await RunTurnAsync(establishment, state, message, customerName, cancellationToken, phone);

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

    private async Task<SalesTurn?> RunTurnAsync(Establishment establishment, SalesState state, string message, string? customerName, CancellationToken cancellationToken, string? phone = null)
    {
        var catalog = await BuildCatalogAsync(establishment, cancellationToken, phone);

        if (state.Step == SalesStep.Idle && !ZestSalesFlow.WantsToOrder(message, catalog))
        {
            return null;
        }

        var ports = new Lookups(establishment, delivery, lookups);

        return await ZestSalesFlow.HandleAsync(state, message, catalog, ports, customerName, cancellationToken);
    }

    /// <summary>A question asked in the middle of a sale is answered without losing the sale.</summary>
    private async Task<string> WithQuestionAnswerAsync(Establishment establishment, string? phone, string message, SalesTurn turn, CancellationToken cancellationToken)
    {
        if (!turn.Unrecognized)
        {
            return turn.Reply;
        }

        var answer = await zest.AnswerAsync(establishment, phone, message, cancellationToken);

        if (answer.Intent is ZestIntent.Unknown && phone is not null && message.Contains('?'))
        {
            // A real question in the middle of a sale that nobody could answer is kept for the store to answer.
            await zest.RecordGapAsync(establishment, message, "whatsapp", cancellationToken);
        }

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
            state.Payment switch { "Pix" => PaymentMethod.Pix, "Cash" => PaymentMethod.Cash, "Checkout" or "Screen" => PaymentMethod.Online, _ => PaymentMethod.CardOnDelivery },
            state.Notes,
            idempotencyKey,
            delivering
                ? new DeliveryAddressRequest(state.Street!, state.Number!, state.Complement, state.District!, state.City!, state.State!, state.PostalCode!, state.Reference)
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
        var scheduled = order.ScheduledFor is { } at
            ? $"\nA loja está fechada agora: seu pedido ficou *agendado para {ScheduleText(establishment, at)}* e você recebe a confirmação quando ela abrir."
            : string.Empty;
        var confirmation = $"Pedido *{order.Code}* recebido! Total: {ZestSalesFlow.Money(order.TotalInCents)}.{scheduled}\nAcompanhe: {urls.TrackingUrl(order.Code)}";

        if (state.Payment == "Screen")
        {
            return new ZestSaleResult(new SalesState(), new ZestSaleReply($"Pedido *{order.Code}* recebido! Total: {ZestSalesFlow.Money(order.TotalInCents)}.\nPague com cartão ou Pix e acompanhe aqui:\n{urls.TrackingUrl(order.Code)}"));
        }

        if (state.Payment == "Checkout")
        {
            var link = await payments.CreateCheckoutLinkAsync(order.Code, cancellationToken);

            return new ZestSaleResult(new SalesState(), new ZestSaleReply(link.IsSuccess
                ? $"{confirmation}\n\nPague pelo Mercado Pago, com cartão ou Pix:\n{link.Value.Url}\nA confirmação é automática."
                : $"{confirmation}\n\nNão consegui gerar o link agora. Você pode pagar com cartão ou Pix nessa página do pedido."));
        }

        if (order.PaymentMethod != PaymentMethod.Pix)
        {
            return new ZestSaleResult(new SalesState(), new ZestSaleReply(confirmation));
        }

        var pix = await payments.CreatePixAsync(order.Code, new CreatePixRequest(null), cancellationToken);

        return pix.IsSuccess && pix.Value.PixQrCode is not null
            ? new ZestSaleResult(new SalesState(), new ZestSaleReply($"{confirmation}\n\nPague pelo QR Code ou pelo código abaixo. A confirmação é automática.", pix.Value.PixQrCodeBase64, pix.Value.PixQrCode))
            : new ZestSaleResult(new SalesState(), new ZestSaleReply($"{confirmation}\n\nO pagamento por Pix será combinado com o estabelecimento."));
    }

    public static string ScheduleText(Establishment establishment, DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, establishment.ResolveTimeZone());
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, establishment.ResolveTimeZone()).Date;
        string[] days = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];
        var day = local.Date == today ? "hoje" : local.Date == today.AddDays(1) ? "amanhã" : days[(int)local.DayOfWeek];

        return $"{day} às {local:HH\\:mm}";
    }

    private async Task<SalesCatalog> BuildCatalogAsync(Establishment establishment, CancellationToken cancellationToken, string? phone = null)
    {
        var categories = await menu.ListCategoriesAsync(establishment.Id, false, cancellationToken);
        var items = await menu.ListItemsAsync(establishment.Id, false, cancellationToken);
        var combos = await menu.ListCombosAsync(establishment.Id, cancellationToken);
        var names = items.ToDictionary(item => item.Id, item => item.Name);

        string? ComboText(Guid itemId)
        {
            var combo = combos.FirstOrDefault(candidate => candidate.MenuItemId == itemId);
            return combo is null
                ? null
                : "Leva: " + string.Join(", ", combo.Lines.Where(line => names.ContainsKey(line.ConstituentMenuItemId))
                    .Select(line => $"{line.Quantity}x {names[line.ConstituentMenuItemId]}"));
        }

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
                            .ToList(),
                        item.IsOnPromotion ? item.CompareAtPriceInCents : null,
                        ComboText(item.Id)))
                    .ToList()))
            .ToList();

        var online = await payments.GetCheckoutOptionsAsync(establishment.TenantId, establishment.Id, false, cancellationToken);
        var now = clock.UtcNow;

        return new SalesCatalog(
            urls.MenuUrl(establishment.Slug),
            sections,
            establishment.IsDeliveryEnabled,
            establishment.IsPickupEnabled,
            establishment.MinimumOrderInCents,
            establishment.City,
            establishment.State,
            establishment.PostalCode,
            online.PixOnline,
            online.CheckoutLink,
            await SavedAddressAsync(establishment, phone, cancellationToken),
            establishment.IsOpenAt(now),
            establishment.IsOpenAt(now) || establishment.NextOpeningAfter(now) is not { } next ? null : ScheduleText(establishment, next));
    }

    /// <summary>The address of the last delivery to this phone, so a returning customer just confirms it.</summary>
    private async Task<SavedAddress?> SavedAddressAsync(Establishment establishment, string? phone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var key = Domain.Common.PhoneNumber.Key(phone);
        var last = await orders.ListAsync(
            order => order.EstablishmentId == establishment.Id && order.CustomerPhoneKey == key && order.FulfillmentType == FulfillmentType.Delivery,
            order => order.CreatedAt,
            cancellationToken,
            descending: true,
            take: 1);

        return last.Count == 0 || last[0].DeliveryStreet is null
            ? null
            : new SavedAddress(
                last[0].DeliveryStreet!,
                last[0].DeliveryNumber!,
                last[0].DeliveryComplement,
                last[0].DeliveryDistrict!,
                last[0].DeliveryCity!,
                last[0].DeliveryState!,
                last[0].DeliveryPostalCode!,
                last[0].DeliveryReference);
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
