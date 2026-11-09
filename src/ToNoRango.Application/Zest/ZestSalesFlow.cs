using System.Globalization;
using ToNoRango.Application.Abstractions;

namespace ToNoRango.Application.Zest;

public enum SalesStep
{
    Idle = 0,
    Category = 1,
    Item = 2,
    Options = 3,
    Quantity = 4,
    CartReview = 5,
    Fulfillment = 6,
    Cep = 7,
    AddressDetails = 8,
    ManualAddress = 9,
    Payment = 10,
    Change = 11,
    Name = 12,
    Confirm = 13
}

public sealed class SalesLine
{
    public Guid ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long UnitPriceInCents { get; set; }
    public int Quantity { get; set; }
    public List<Guid> OptionIds { get; set; } = [];
    public string? OptionsLabel { get; set; }
}

/// <summary>The sale in progress. It is stored with the conversation between messages.</summary>
public sealed class SalesState
{
    public SalesStep Step { get; set; }
    public int Misses { get; set; }
    public int? CategoryIndex { get; set; }
    public Guid? PendingItemId { get; set; }
    public int PendingGroup { get; set; }
    public List<Guid> PendingOptions { get; set; } = [];
    public List<string> PendingLabels { get; set; } = [];
    public long PendingExtraInCents { get; set; }
    public int? PendingQuantity { get; set; }
    public List<SalesLine> Cart { get; set; } = [];
    public string? Fulfillment { get; set; }
    public string? Street { get; set; }
    public string? Number { get; set; }
    public string? Complement { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public long DeliveryFeeInCents { get; set; }
    public string? Payment { get; set; }
    public string? Notes { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public long SubtotalInCents => Cart.Sum(line => line.UnitPriceInCents * line.Quantity);
}

public sealed record SalesOption(Guid Id, string Name, long PriceInCents, bool IsAvailable);

public sealed record SalesGroup(string Name, int Min, int Max, IReadOnlyList<SalesOption> Options);

public sealed record SalesItem(Guid Id, string Name, string? Description, long PriceInCents, bool IsAvailable, IReadOnlyList<SalesGroup> Groups);

public sealed record SalesCategory(string Name, IReadOnlyList<SalesItem> Items);

public sealed record SalesCatalog(
    string MenuUrl,
    IReadOnlyList<SalesCategory> Categories,
    bool DeliveryEnabled,
    bool PickupEnabled,
    long MinimumOrderInCents,
    string? City,
    string? State,
    string? PostalCode,
    bool PixOnline = false,
    bool CheckoutLink = false);

public interface ISalesLookups
{
    Task<CepData?> FindCepAsync(string cep, CancellationToken cancellationToken);

    /// <summary>Returns the fee, or null with a message when the district is not served.</summary>
    Task<(long? FeeInCents, string? Error)> QuoteAsync(string district, CancellationToken cancellationToken);
}

public sealed record SalesTurn(SalesState State, string Reply, bool Checkout = false, bool Unrecognized = false);

/// <summary>
/// The conversation that sells through WhatsApp. It only collects and confirms the order: prices,
/// availability and totals are validated again by the order service when the customer confirms.
/// </summary>
public static class ZestSalesFlow
{
    private static readonly string[] OrderPhrases =
    [
        "quero pedir", "fazer pedido", "fazer um pedido", "quero comprar", "quero fazer um pedido",
        "vou pedir", "pedir agora", "pedir aqui", "gostaria de pedir"
    ];

    private static readonly string[] BuyVerbs = ["quero", "queria", "me ve", "me manda", "vou querer", "manda", "gostaria"];

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.Ordinal)
    {
        ["um"] = 1, ["uma"] = 1, ["dois"] = 2, ["duas"] = 2, ["tres"] = 3, ["quatro"] = 4, ["cinco"] = 5,
        ["seis"] = 6, ["sete"] = 7, ["oito"] = 8, ["nove"] = 9, ["dez"] = 10
    };

    public static bool WantsToOrder(string message, SalesCatalog catalog)
    {
        var text = ZestBrain.Normalize(message);
        return HasAny(text, OrderPhrases) || (HasAny(text, BuyVerbs) && FindMentionedItem(text, catalog) is not null);
    }

    public static async Task<SalesTurn> HandleAsync(
        SalesState state,
        string message,
        SalesCatalog catalog,
        ISalesLookups lookups,
        string? customerName,
        CancellationToken cancellationToken)
    {
        var turn = await TakeTurnAsync(state, message, catalog, lookups, customerName, cancellationToken);

        if (!turn.Unrecognized)
        {
            turn.State.Misses = 0;
        }

        return turn;
    }

    private static async Task<SalesTurn> TakeTurnAsync(
        SalesState state,
        string message,
        SalesCatalog catalog,
        ISalesLookups lookups,
        string? customerName,
        CancellationToken cancellationToken)
    {
        var text = ZestBrain.Normalize(message);

        if (HasAny(text, "cancelar", "cancela", "desistir", "desisto", "limpar carrinho"))
        {
            return new SalesTurn(new SalesState(), "Tudo bem, cancelei este pedido. Quando quiser é só escrever *quero pedir*.");
        }

        if (state.Step != SalesStep.Idle && text is "carrinho" or "ver carrinho")
        {
            return Reply(state, $"{CartText(state)}\n\n{Prompt(state, catalog)}");
        }

        switch (state.Step)
        {
            case SalesStep.Idle:
                return Start(state, text, catalog);
            case SalesStep.Category:
                return ChooseCategory(state, text, catalog);
            case SalesStep.Item:
                return ChooseItem(state, text, catalog);
            case SalesStep.Options:
                return ChooseOptions(state, text, catalog);
            case SalesStep.Quantity:
                return ChooseQuantity(state, text, catalog);
            case SalesStep.CartReview:
                return ReviewCart(state, text, catalog, customerName);
            case SalesStep.Fulfillment:
                return ChooseFulfillment(state, text, catalog, customerName);
            case SalesStep.Cep:
                return await ReadCepAsync(state, text, catalog, lookups, cancellationToken);
            case SalesStep.AddressDetails:
                return ReadAddressDetails(state, message, catalog, customerName);
            case SalesStep.ManualAddress:
                return await ReadManualAddressAsync(state, message, catalog, lookups, customerName, cancellationToken);
            case SalesStep.Payment:
                return ChoosePayment(state, text, catalog, customerName);
            case SalesStep.Change:
                return ReadChange(state, message, text, catalog, customerName);
            case SalesStep.Name:
                state.CustomerName = message.Trim().Length is >= 2 and <= 120 ? message.Trim() : null;
                return state.CustomerName is null
                    ? Reply(state, "Me diz seu nome, por favor.")
                    : ToConfirm(state, catalog);
            case SalesStep.Confirm:
                return Confirm(state, text, catalog);
            default:
                return Start(new SalesState(), text, catalog);
        }
    }

    public static string Prompt(SalesState state, SalesCatalog catalog) => state.Step switch
    {
        SalesStep.Category => CategoriesText(catalog),
        SalesStep.Item => ItemsText(catalog.Categories[state.CategoryIndex ?? 0]),
        SalesStep.Options => OptionsText(state, catalog),
        SalesStep.Quantity => "Quantas unidades?",
        SalesStep.CartReview => "Responda *1* para adicionar mais itens ou *2* para finalizar.",
        SalesStep.Fulfillment => "Responda *1* para entrega ou *2* para retirada.",
        SalesStep.Cep => "Qual o seu CEP? Se não souber, escreva *sem cep*.",
        SalesStep.AddressDetails => "Qual o número e o complemento?",
        SalesStep.ManualAddress => "Me manda assim: *rua, número, bairro*.",
        SalesStep.Payment => PaymentText(catalog),
        SalesStep.Change => "Precisa de troco? Escreva para quanto ou *não*.",
        SalesStep.Name => "Qual o seu nome?",
        SalesStep.Confirm => "Responda *1* para confirmar ou *2* para alterar.",
        _ => string.Empty
    };

    public static string CartText(SalesState state)
    {
        if (state.Cart.Count == 0)
        {
            return "Seu carrinho está vazio.";
        }

        var lines = state.Cart.Select((line, index) =>
            $"{index + 1}. {line.Quantity}x {line.Name}{(line.OptionsLabel is null ? string.Empty : $" ({line.OptionsLabel})")} — {Money(line.UnitPriceInCents * line.Quantity)}");

        return $"*Seu pedido*\n{string.Join('\n', lines)}\nSubtotal: {Money(state.SubtotalInCents)}";
    }

    public static string Money(long cents) =>
        $"R$ {(cents / 100m).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}";

    private static SalesTurn Reply(SalesState state, string text, bool unrecognized = false) => new(state, text, Unrecognized: unrecognized);

    /// <summary>The same question twice in a row is what reads as a robot, so the second miss changes the sentence.</summary>
    private static SalesTurn Retry(SalesState state, SalesCatalog catalog, string hint)
    {
        state.Misses++;

        return state.Misses == 1
            ? Reply(state, $"{hint}\n\n{Prompt(state, catalog)}", unrecognized: true)
            : Reply(state, $"{hint} Pode me responder com uma das opções que mandei? Se preferir falar com alguém, é só escrever *atendente*.", unrecognized: true);
    }

    /// <summary>An extra that is not on the menu gets a plain no; the customer is never left guessing.</summary>
    private static string NotUnderstood(string text) =>
        HasAny(text, "extra", "adicional", "acrescenta", "acrescentar", "coloca mais", "bota mais")
            ? "Esse adicional não está no nosso cardápio, então não consigo incluir."
            : "Não entendi.";

    private static SalesTurn Start(SalesState state, string text, SalesCatalog catalog)
    {
        var mentioned = FindMentionedItem(text, catalog);

        if (mentioned is { } item && item.IsAvailable)
        {
            state.PendingQuantity = ParseQuantityBefore(text);
            return BeginItem(state, item, catalog, "Boa escolha!");
        }

        var sellable = SellableCategories(catalog);

        if (sellable.Count == 0)
        {
            return Reply(new SalesState(), $"No momento não temos itens disponíveis. Veja o cardápio em {catalog.MenuUrl}");
        }

        if (sellable.Count == 1)
        {
            state.CategoryIndex = IndexOf(catalog, sellable[0]);
            state.Step = SalesStep.Item;
            return Reply(state, ItemsText(sellable[0]));
        }

        state.Step = SalesStep.Category;
        return Reply(state, CategoriesText(catalog));
    }

    private static SalesTurn ChooseCategory(SalesState state, string text, SalesCatalog catalog)
    {
        var mentioned = FindMentionedItem(text, catalog);

        if (mentioned is { IsAvailable: true })
        {
            state.PendingQuantity = ParseQuantityBefore(text);
            return BeginItem(state, mentioned, catalog, "Boa escolha!");
        }

        var sellable = SellableCategories(catalog);
        var category = Pick(sellable, candidate => candidate.Name, text);

        if (category is null)
        {
            return Retry(state, catalog, "Não encontrei essa categoria.");
        }

        state.CategoryIndex = IndexOf(catalog, category);
        state.Step = SalesStep.Item;

        return Reply(state, ItemsText(category));
    }

    private static SalesTurn ChooseItem(SalesState state, string text, SalesCatalog catalog)
    {
        if (HasAny(text, "voltar"))
        {
            state.Step = SalesStep.Category;
            return Reply(state, CategoriesText(catalog));
        }

        var category = catalog.Categories[state.CategoryIndex ?? 0];
        var available = category.Items.Where(item => item.IsAvailable).ToList();
        var (quantity, rest) = SplitLeadingQuantity(text);
        var item = Pick(available, candidate => candidate.Name, rest) ?? FindMentionedItem(text, catalog);

        if (item is null || !item.IsAvailable)
        {
            return Retry(state, catalog, "Não encontrei esse item.");
        }

        state.PendingQuantity = quantity;
        return BeginItem(state, item, catalog, null);
    }

    private static SalesTurn BeginItem(SalesState state, SalesItem item, SalesCatalog catalog, string? intro)
    {
        state.PendingItemId = item.Id;
        state.PendingGroup = -1;
        state.PendingOptions = [];
        state.PendingLabels = [];
        state.PendingExtraInCents = 0;

        return Advance(state, item, catalog, intro is null ? item.Name : $"{intro} {item.Name}.");
    }

    /// <summary>Moves to the next option group that needs an answer, or on to the quantity.</summary>
    private static SalesTurn Advance(SalesState state, SalesItem item, SalesCatalog catalog, string? intro)
    {
        state.PendingGroup++;

        while (state.PendingGroup < item.Groups.Count)
        {
            var group = item.Groups[state.PendingGroup];

            if (group.Options.Any(option => option.IsAvailable))
            {
                state.Step = SalesStep.Options;
                var prompt = OptionsText(state, catalog);
                return Reply(state, intro is null ? prompt : $"{intro}\n\n{prompt}");
            }

            if (group.Min > 0)
            {
                ClearPending(state);
                state.Step = state.CategoryIndex is null ? SalesStep.Category : SalesStep.Item;
                return Reply(state, $"{item.Name} está sem {group.Name.ToLowerInvariant()} disponível no momento.\n\n{Prompt(state, catalog)}");
            }

            state.PendingGroup++;
        }

        if (state.PendingQuantity is null)
        {
            state.Step = SalesStep.Quantity;
            return Reply(state, intro is null ? "Quantas unidades?" : $"{intro}\n\nQuantas unidades?");
        }

        return AddToCart(state, item, catalog);
    }

    private static SalesTurn ChooseOptions(SalesState state, string text, SalesCatalog catalog)
    {
        var item = FindItem(catalog, state.PendingItemId);

        if (item is null)
        {
            return Reply(new SalesState { Step = SalesStep.Category }, CategoriesText(catalog));
        }

        var group = item.Groups[state.PendingGroup];
        var available = group.Options.Where(option => option.IsAvailable).ToList();

        if (group.Min == 0 && (text is "0" || HasAny(text, "nao", "nenhum", "nenhuma", "pular", "sem")))
        {
            return Advance(state, item, catalog, null);
        }

        var chosen = ParseOptions(text, available);

        if (chosen.Count < Math.Max(group.Min, 1) || chosen.Count > group.Max)
        {
            var range = group.Min == group.Max ? $"{group.Max}" : $"de {Math.Max(group.Min, 1)} a {group.Max}";
            return Retry(state, catalog, $"Escolha {range} opção(ões), pelos números.");
        }

        state.PendingOptions.AddRange(chosen.Select(option => option.Id));
        state.PendingLabels.AddRange(chosen.Select(option => option.Name));
        state.PendingExtraInCents += chosen.Sum(option => option.PriceInCents);

        return Advance(state, item, catalog, null);
    }

    private static SalesTurn ChooseQuantity(SalesState state, string text, SalesCatalog catalog)
    {
        var item = FindItem(catalog, state.PendingItemId);
        var quantity = ParseStandaloneQuantity(text);

        if (item is null)
        {
            return Reply(new SalesState { Step = SalesStep.Category }, CategoriesText(catalog));
        }

        if (quantity is null or < 1 or > 99)
        {
            return Retry(state, catalog, "Me diga uma quantidade de 1 a 99.");
        }

        state.PendingQuantity = quantity;

        return AddToCart(state, item, catalog);
    }

    private static SalesTurn AddToCart(SalesState state, SalesItem item, SalesCatalog catalog)
    {
        var line = new SalesLine
        {
            ItemId = item.Id,
            Name = item.Name,
            UnitPriceInCents = item.PriceInCents + state.PendingExtraInCents,
            Quantity = Math.Clamp(state.PendingQuantity ?? 1, 1, 99),
            OptionIds = [.. state.PendingOptions],
            OptionsLabel = state.PendingLabels.Count == 0 ? null : string.Join(", ", state.PendingLabels)
        };

        state.Cart.Add(line);
        ClearPending(state);
        state.Step = SalesStep.CartReview;

        return Reply(state, $"Anotado!\n\n{CartText(state)}\n\n*1* Adicionar mais itens\n*2* Finalizar pedido");
    }

    private static SalesTurn ReviewCart(SalesState state, string text, SalesCatalog catalog, string? customerName)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2 && parts[0] is "remover" or "tirar" && int.TryParse(parts[1], out var index) && index >= 1 && index <= state.Cart.Count)
        {
            state.Cart.RemoveAt(index - 1);

            if (state.Cart.Count == 0)
            {
                state.Step = SalesStep.Category;
                return Reply(state, $"Item removido. Seu carrinho ficou vazio.\n\n{CategoriesText(catalog)}");
            }

            return Reply(state, $"Item removido.\n\n{CartText(state)}\n\n*1* Adicionar mais itens\n*2* Finalizar pedido");
        }

        if (text == "1" || HasAny(text, "mais", "adicionar"))
        {
            state.Step = SalesStep.Category;
            return Reply(state, CategoriesText(catalog));
        }

        if (text == "2" || HasAny(text, "finalizar", "fechar", "so isso", "pronto", "acabou", "e isso"))
        {
            if (state.SubtotalInCents < catalog.MinimumOrderInCents)
            {
                return Reply(state, $"O pedido mínimo é {Money(catalog.MinimumOrderInCents)}. Faltam {Money(catalog.MinimumOrderInCents - state.SubtotalInCents)}.\n\n*1* Adicionar mais itens");
            }

            return ToFulfillment(state, catalog, customerName);
        }

        var mentioned = FindMentionedItem(text, catalog);

        if (mentioned is { IsAvailable: true })
        {
            state.PendingQuantity = ParseQuantityBefore(text);
            state.CategoryIndex = null;
            return BeginItem(state, mentioned, catalog, null);
        }

        return Retry(state, catalog, NotUnderstood(text));
    }

    private static SalesTurn ToFulfillment(SalesState state, SalesCatalog catalog, string? customerName)
    {
        if (catalog.DeliveryEnabled && catalog.PickupEnabled)
        {
            state.Step = SalesStep.Fulfillment;
            return Reply(state, "Como você prefere receber?\n\n*1* Entrega\n*2* Retirada no local");
        }

        return catalog.DeliveryEnabled ? ToDelivery(state, catalog) : ToPickup(state, catalog, customerName);
    }

    private static SalesTurn ChooseFulfillment(SalesState state, string text, SalesCatalog catalog, string? customerName)
    {
        if (text == "1" || HasAny(text, "entrega", "entregar", "delivery"))
        {
            return ToDelivery(state, catalog);
        }

        if (text == "2" || HasAny(text, "retirada", "retirar", "buscar", "pego"))
        {
            return ToPickup(state, catalog, customerName);
        }

        return Retry(state, catalog, "Não entendi.");
    }

    private static SalesTurn ToDelivery(SalesState state, SalesCatalog catalog)
    {
        state.Fulfillment = "Delivery";
        state.Step = SalesStep.Cep;

        return Reply(state, Prompt(state, catalog));
    }

    private static SalesTurn ToPickup(SalesState state, SalesCatalog catalog, string? customerName)
    {
        state.Fulfillment = "Pickup";
        state.DeliveryFeeInCents = 0;

        return ToPayment(state, catalog, customerName);
    }

    private static async Task<SalesTurn> ReadCepAsync(SalesState state, string text, SalesCatalog catalog, ISalesLookups lookups, CancellationToken cancellationToken)
    {
        if (HasAny(text, "sem cep", "nao sei", "nao tenho"))
        {
            state.Step = SalesStep.ManualAddress;
            return Reply(state, Prompt(state, catalog));
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());

        if (digits.Length != 8)
        {
            return Retry(state, catalog, "O CEP tem 8 números.");
        }

        CepData? found;

        try
        {
            found = await lookups.FindCepAsync(digits, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            found = null;
        }

        if (found is null || string.IsNullOrWhiteSpace(found.Street) || string.IsNullOrWhiteSpace(found.District))
        {
            state.PostalCode = digits;
            state.City = found?.City;
            state.State = found?.State;
            state.Step = SalesStep.ManualAddress;
            return Reply(state, $"Não consegui completar o endereço por esse CEP.\n\n{Prompt(state, catalog)}");
        }

        var quote = await lookups.QuoteAsync(found.District, cancellationToken);

        if (quote.FeeInCents is null)
        {
            return Reply(state, $"{quote.Error}\n\nTem outro CEP? Ou escreva *cancelar*.");
        }

        state.PostalCode = digits;
        state.Street = found.Street;
        state.District = found.District;
        state.City = found.City;
        state.State = found.State;
        state.DeliveryFeeInCents = quote.FeeInCents.Value;
        state.Step = SalesStep.AddressDetails;

        return Reply(state, $"{found.Street}, {found.District}. Taxa de entrega: {Money(quote.FeeInCents.Value)}.\n\nQual o número e o complemento?");
    }

    private static SalesTurn ReadAddressDetails(SalesState state, string message, SalesCatalog catalog, string? customerName)
    {
        var parts = message.Trim().Split([' ', ','], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0 || parts[0].Length > 20 || !(parts[0].Any(char.IsDigit) || ZestBrain.Normalize(parts[0]) is "sn"))
        {
            return Retry(state, catalog, "Preciso do número da casa ou prédio.");
        }

        state.Number = parts[0];
        state.Complement = parts.Length > 1 ? Truncate(parts[1], 100) : null;

        return ToPayment(state, catalog, customerName);
    }

    private static async Task<SalesTurn> ReadManualAddressAsync(
        SalesState state,
        string message,
        SalesCatalog catalog,
        ISalesLookups lookups,
        string? customerName,
        CancellationToken cancellationToken)
    {
        var parts = message.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 3 || parts[1].Length > 20)
        {
            return Retry(state, catalog, "Não consegui separar o endereço.");
        }

        var quote = await lookups.QuoteAsync(parts[2], cancellationToken);

        if (quote.FeeInCents is null)
        {
            return Reply(state, $"{quote.Error}\n\nTem outro endereço? Ou escreva *cancelar*.");
        }

        state.Street = Truncate(parts[0], 200);
        state.Number = parts[1];
        state.District = Truncate(parts[2], 80);
        state.Complement = parts.Length > 3 ? Truncate(string.Join(", ", parts[3..]), 100) : null;
        state.City ??= catalog.City;
        state.State ??= catalog.State;
        state.PostalCode ??= catalog.PostalCode;
        state.DeliveryFeeInCents = quote.FeeInCents.Value;

        if (string.IsNullOrWhiteSpace(state.City) || string.IsNullOrWhiteSpace(state.State) || string.IsNullOrWhiteSpace(state.PostalCode))
        {
            state.Step = SalesStep.Cep;
            return Reply(state, "Para este endereço preciso do CEP. Qual é?");
        }

        return ToPayment(state, catalog, customerName, $"Taxa de entrega: {Money(state.DeliveryFeeInCents)}.");
    }

    private static SalesTurn ToPayment(SalesState state, SalesCatalog catalog, string? customerName, string? intro = null)
    {
        state.CustomerName ??= string.IsNullOrWhiteSpace(customerName) ? null : Truncate(customerName.Trim(), 120);
        state.Step = SalesStep.Payment;

        return Reply(state, intro is null ? PaymentText(catalog) : $"{intro}\n\n{PaymentText(catalog)}");
    }

    private static SalesTurn ChoosePayment(SalesState state, string text, SalesCatalog catalog, string? customerName)
    {
        var choices = PaymentChoices(catalog);
        var chosen = int.TryParse(text, out var number) && number >= 1 && number <= choices.Count
            ? choices[number - 1].Key
            : PaymentByWords(text, catalog);

        if (chosen is null)
        {
            return Retry(state, catalog, "Não entendi.");
        }

        state.Payment = chosen;

        if (chosen != "Cash")
        {
            return AfterPayment(state, catalog);
        }

        state.Step = SalesStep.Change;
        return Reply(state, Prompt(state, catalog));
    }

    private static string? PaymentByWords(string text, SalesCatalog catalog)
    {
        if (catalog.CheckoutLink && HasAny(text, "link", "mercado pago"))
        {
            return "Checkout";
        }

        if (catalog.CheckoutLink && HasAny(text, "tela", "site", "pagina"))
        {
            return "Screen";
        }

        if (HasAny(text, "pix"))
        {
            return "Pix";
        }

        if (HasAny(text, "dinheiro"))
        {
            return "Cash";
        }

        if (HasAny(text, "maquininha", "na entrega"))
        {
            return "CardOnDelivery";
        }

        return HasAny(text, "cartao", "credito", "debito") ? (catalog.CheckoutLink ? "Checkout" : "CardOnDelivery") : null;
    }

    private static SalesTurn ReadChange(SalesState state, string message, string text, SalesCatalog catalog, string? customerName)
    {
        if (!HasAny(text, "nao", "sem troco", "nao precisa"))
        {
            state.Notes = $"Troco para {Truncate(message.Trim(), 40)}";
        }

        return AfterPayment(state, catalog);
    }

    private static SalesTurn AfterPayment(SalesState state, SalesCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(state.CustomerName))
        {
            state.Step = SalesStep.Name;
            return Reply(state, Prompt(state, catalog));
        }

        return ToConfirm(state, catalog);
    }

    private static SalesTurn ToConfirm(SalesState state, SalesCatalog catalog)
    {
        state.Step = SalesStep.Confirm;

        var receive = state.Fulfillment == "Delivery"
            ? $"Entrega em {state.Street}, {state.Number}{(state.Complement is null ? string.Empty : $" {state.Complement}")} — {state.District}\nTaxa: {Money(state.DeliveryFeeInCents)}"
            : "Retirada no local";
        var payment = state.Payment switch { "Pix" => "Pix", "Cash" => "Dinheiro", "Checkout" => "Link do Mercado Pago", "Screen" => "Tela do pedido", _ => "Cartão na entrega" };

        return Reply(state, $"{CartText(state)}\n{receive}\nPagamento: {payment}\n*Total: {Money(state.SubtotalInCents + state.DeliveryFeeInCents)}*\n\n*1* Confirmar pedido\n*2* Alterar");
    }

    private static SalesTurn Confirm(SalesState state, string text, SalesCatalog catalog)
    {
        if (text == "1" || HasAny(text, "confirmar", "confirmo", "sim", "pode"))
        {
            return new SalesTurn(state, string.Empty, Checkout: true);
        }

        if (text == "2" || HasAny(text, "alterar", "mudar", "nao"))
        {
            state.Step = SalesStep.CartReview;
            return Reply(state, $"{CartText(state)}\n\nPara tirar um item escreva *remover 1*.\n*1* Adicionar mais itens\n*2* Finalizar pedido");
        }

        return Retry(state, catalog, NotUnderstood(text));
    }

    private static void ClearPending(SalesState state)
    {
        state.PendingItemId = null;
        state.PendingGroup = 0;
        state.PendingOptions = [];
        state.PendingLabels = [];
        state.PendingExtraInCents = 0;
        state.PendingQuantity = null;
    }

    private static string CategoriesText(SalesCatalog catalog)
    {
        var lines = SellableCategories(catalog).Select((category, index) => $"*{index + 1}* {category.Name}");
        return $"O que você vai querer?\n\n{string.Join('\n', lines)}\n\nResponda o número ou peça direto, tipo *2 {FirstItemName(catalog)}*.";
    }

    private static string FirstItemName(SalesCatalog catalog) =>
        catalog.Categories.SelectMany(category => category.Items).FirstOrDefault(item => item.IsAvailable)?.Name.ToLowerInvariant() ?? "item";

    private static string ItemsText(SalesCategory category)
    {
        var lines = category.Items.Where(item => item.IsAvailable).Select((item, index) => $"*{index + 1}* {item.Name} — {Money(item.PriceInCents)}");
        return $"*{category.Name}*\n\n{string.Join('\n', lines)}\n\nResponda o número do item.";
    }

    private static string OptionsText(SalesState state, SalesCatalog catalog)
    {
        var item = FindItem(catalog, state.PendingItemId);

        if (item is null || state.PendingGroup < 0 || state.PendingGroup >= item.Groups.Count)
        {
            return string.Empty;
        }

        var group = item.Groups[state.PendingGroup];
        var lines = group.Options.Where(option => option.IsAvailable)
            .Select((option, index) => $"*{index + 1}* {option.Name}{(option.PriceInCents > 0 ? $" (+{Money(option.PriceInCents)})" : string.Empty)}");
        var hint = group.Max > 1 ? $"Pode escolher até {group.Max}, separando por vírgula." : "Responda o número.";
        var skip = group.Min == 0 ? " Ou *0* para nenhum." : string.Empty;

        return $"*{group.Name}*\n\n{string.Join('\n', lines)}\n\n{hint}{skip}";
    }

    private static List<(string Key, string Label)> PaymentChoices(SalesCatalog catalog)
    {
        List<(string Key, string Label)> choices = [("Pix", catalog.PixOnline ? "Pix aqui no WhatsApp" : "Pix")];

        if (catalog.CheckoutLink)
        {
            choices.Add(("Checkout", "Link do Mercado Pago (cartão ou Pix)"));
            choices.Add(("Screen", "Tela do pedido (cartão ou Pix)"));
        }

        choices.Add(("Cash", "Dinheiro"));
        choices.Add(("CardOnDelivery", "Cartão na entrega"));

        return choices;
    }

    private static string PaymentText(SalesCatalog catalog) =>
        $"Como vai pagar?\n\n{string.Join('\n', PaymentChoices(catalog).Select((choice, index) => $"*{index + 1}* {choice.Label}"))}";

    private static List<SalesCategory> SellableCategories(SalesCatalog catalog) =>
        catalog.Categories.Where(category => category.Items.Any(item => item.IsAvailable)).ToList();

    private static int IndexOf(SalesCatalog catalog, SalesCategory category) => catalog.Categories.ToList().IndexOf(category);

    private static SalesItem? FindItem(SalesCatalog catalog, Guid? id) =>
        catalog.Categories.SelectMany(category => category.Items).FirstOrDefault(item => item.Id == id);

    /// <summary>Finds the item whose whole name appears in the message, preferring the longest name.</summary>
    private static SalesItem? FindMentionedItem(string text, SalesCatalog catalog) => catalog.Categories
        .SelectMany(category => category.Items)
        .Select(item => (Item: item, Name: ZestBrain.Normalize(item.Name)))
        .Where(candidate => candidate.Name.Length >= 3 && Has(text, candidate.Name))
        .OrderByDescending(candidate => candidate.Name.Length)
        .Select(candidate => candidate.Item)
        .FirstOrDefault();

    private static T? Pick<T>(IReadOnlyList<T> list, Func<T, string> name, string text)
        where T : class
    {
        if (int.TryParse(text, out var number))
        {
            return number >= 1 && number <= list.Count ? list[number - 1] : null;
        }

        var exact = list.FirstOrDefault(candidate => ZestBrain.Normalize(name(candidate)) == text);

        if (exact is not null || text.Length < 3)
        {
            return exact;
        }

        var partial = list.Where(candidate => Has(ZestBrain.Normalize(name(candidate)), text) || Has(text, ZestBrain.Normalize(name(candidate)))).ToList();

        return partial.Count == 1 ? partial[0] : null;
    }

    private static List<SalesOption> ParseOptions(string text, IReadOnlyList<SalesOption> available)
    {
        var chosen = new List<SalesOption>();

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out var number) && number >= 1 && number <= available.Count)
            {
                chosen.Add(available[number - 1]);
            }
        }

        chosen.AddRange(available.Where(option => !chosen.Contains(option) && Has(text, ZestBrain.Normalize(option.Name))));

        return chosen.Distinct().ToList();
    }

    /// <summary>"2 x burger" or "dois burger" becomes a quantity and the remaining words.</summary>
    private static (int? Quantity, string Remainder) SplitLeadingQuantity(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return (null, text);
        }

        var quantity = ParseNumberWord(parts[0]);

        if (quantity is null)
        {
            return (null, text);
        }

        var skip = parts[1] == "x" && parts.Length > 2 ? 2 : 1;

        return (quantity, string.Join(' ', parts[skip..]));
    }

    private static int? ParseQuantityBefore(string text)
    {
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseNumberWord(token) is { } quantity and >= 1 and <= 99)
            {
                return quantity;
            }

            if (token is "x")
            {
                break;
            }
        }

        return null;
    }

    private static int? ParseStandaloneQuantity(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ParseNumberWord).FirstOrDefault(value => value is not null);

    private static int? ParseNumberWord(string token) =>
        int.TryParse(token, out var number) ? number : NumberWords.TryGetValue(token, out var word) ? word : null;

    private static bool Has(string text, string phrase) => $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static bool HasAny(string text, params string[] phrases) => phrases.Any(phrase => Has(text, phrase));

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
