using System.Globalization;
using System.Text;
using ToNoRango.Domain.Establishments;

namespace ToNoRango.Application.Zest;

public sealed record ZestMenuItem(string Name, long PriceInCents, string Category, bool IsAvailable, string? Description);

public sealed record ZestKnowledge(string Topic, string Answer, string Keywords);

public sealed record ZestZone(string District, long FeeInCents);

public sealed record ZestOrder(string Code, string StatusLabel, string TrackingUrl);

public sealed record ZestContext(
    string EstablishmentName,
    string MenuUrl,
    string? Address,
    bool DeliveryEnabled,
    bool PickupEnabled,
    long FlatDeliveryFeeInCents,
    long MinimumOrderInCents,
    string PaymentMethods,
    IReadOnlyList<ZestZone> Zones,
    IReadOnlyList<ZestMenuItem> Items,
    IReadOnlyList<ZestKnowledge> Knowledge,
    ZestOrder? LastOrder);

public enum ZestIntent
{
    Handoff,
    OrderStatus,
    Knowledge,
    ItemPrice,
    Delivery,
    Menu,
    Address,
    Payment,
    Greeting,
    Thanks,
    Unknown
}

public sealed record ZestReply(ZestIntent Intent, string Text);

/// <summary>
/// The rule engine of an establishment's ZEST. It answers only from the context it is given, which
/// is built per establishment, and never executes an operation on the customer's behalf.
/// </summary>
public static class ZestBrain
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "com", "sem", "para", "por", "dos", "das", "uma", "um", "the", "tem", "que", "qual", "quanto", "custa", "valor", "preco"
    };

    public static string Fallback(ZestContext context) =>
        $"Ainda não sei responder isso. Veja o cardápio em {context.MenuUrl} ou escreva *atendente* para falar com a equipe.";

    public static bool IsHandoff(string message) =>
        HasAny(Normalize(message), "atendente", "humano", "falar com alguem", "falar com uma pessoa");

    public static ZestReply Answer(ZestContext context, string message)
    {
        var text = Normalize(message);

        if (IsHandoff(message))
        {
            return new ZestReply(ZestIntent.Handoff, "Certo! Vou chamar alguém da equipe para continuar com você.");
        }

        if (HasAny(text, "meu pedido", "status", "cade", "ja saiu", "acompanhar", "rastrear"))
        {
            return new ZestReply(ZestIntent.OrderStatus, context.LastOrder is { } order
                ? $"Seu pedido {order.Code} está {order.StatusLabel}. Acompanhe: {order.TrackingUrl}"
                : $"Não encontrei pedidos recentes neste número. Para pedir, acesse {context.MenuUrl}");
        }

        var knowledge = FindKnowledge(context, text);

        if (knowledge is not null)
        {
            return new ZestReply(ZestIntent.Knowledge, knowledge.Answer);
        }

        var itemReply = AnswerItem(context, text);

        if (itemReply is not null)
        {
            return new ZestReply(ZestIntent.ItemPrice, itemReply);
        }

        if (HasAny(text, "entrega", "entregam", "entregar", "taxa", "frete", "delivery"))
        {
            return new ZestReply(ZestIntent.Delivery, AnswerDelivery(context, text));
        }

        if (HasAny(text, "cardapio", "menu", "o que tem", "opcoes", "quero pedir", "fazer pedido", "fazer um pedido"))
        {
            var categories = context.Items.Select(item => item.Category).Distinct().Take(8).ToList();
            var summary = categories.Count > 0 ? $" Temos: {string.Join(", ", categories)}." : string.Empty;
            return new ZestReply(ZestIntent.Menu, $"Nosso cardápio está aqui: {context.MenuUrl}.{summary}");
        }

        if (HasAny(text, "endereco", "onde fica", "localizacao", "onde voces"))
        {
            return new ZestReply(ZestIntent.Address, string.IsNullOrWhiteSpace(context.Address)
                ? "Ainda não tenho o endereço cadastrado. Escreva *atendente* para falar com a equipe."
                : $"Estamos em: {context.Address}");
        }

        if (HasAny(text, "pix", "cartao", "pagamento", "pagar", "dinheiro"))
        {
            return new ZestReply(ZestIntent.Payment, $"Aceitamos: {context.PaymentMethods}.");
        }

        if (HasAny(text, "oi", "ola", "bom dia", "boa tarde", "boa noite", "eai", "e ai"))
        {
            return new ZestReply(ZestIntent.Greeting, $"Olá! Aqui é a ZEST do {context.EstablishmentName}. Posso mostrar o cardápio, preços, taxa de entrega ou o status do seu pedido.");
        }

        if (HasAny(text, "obrigado", "obrigada", "valeu"))
        {
            return new ZestReply(ZestIntent.Thanks, "Por nada! Qualquer coisa é só chamar.");
        }

        return new ZestReply(ZestIntent.Unknown, Fallback(context));
    }

    public static string Normalize(string value)
    {
        // The app runs in invariant globalization, where string.Normalize does not strip accents.
        var lowered = (value ?? string.Empty).ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);

        foreach (var character in lowered)
        {
            var plain = Establishment.RemoveDiacritics(character);
            builder.Append(char.IsLetterOrDigit(plain) ? plain : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool Has(string text, string phrase) => $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static bool HasAny(string text, params string[] phrases) => phrases.Any(phrase => Has(text, phrase));

    private static ZestKnowledge? FindKnowledge(ZestContext context, string text) => context.Knowledge
        .Select(entry => (Entry: entry, Score: ScoreKnowledge(entry, text)))
        .Where(candidate => candidate.Score > 0)
        .OrderByDescending(candidate => candidate.Score)
        .Select(candidate => candidate.Entry)
        .FirstOrDefault();

    private static int ScoreKnowledge(ZestKnowledge entry, string text)
    {
        var keywords = entry.Keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(keyword => keyword.Length > 0)
            .Count(keyword => Has(text, keyword));

        return keywords > 0 || Has(text, Normalize(entry.Topic)) ? keywords + 1 : 0;
    }

    private static string? AnswerItem(ZestContext context, string text)
    {
        var asksPrice = HasAny(text, "quanto", "preco", "valor", "custa");
        var scored = context.Items
            .Select(item =>
            {
                var tokens = Tokens(item.Name);
                var hits = tokens.Count(token => Has(text, token));
                return (Item: item, Hits: hits, Full: tokens.Count > 0 && hits == tokens.Count);
            })
            .Where(candidate => candidate.Hits > 0)
            .OrderByDescending(candidate => candidate.Full)
            .ThenByDescending(candidate => candidate.Hits)
            .ToList();

        if (scored.Count == 0)
        {
            return null;
        }

        if (scored[0].Full)
        {
            return Describe(scored[0].Item);
        }

        return asksPrice
            ? string.Join("\n", scored.Take(5).Select(candidate => Describe(candidate.Item)))
            : null;
    }

    private static string Describe(ZestMenuItem item) => item.IsAvailable
        ? $"{item.Name}: {Money(item.PriceInCents)}{(string.IsNullOrWhiteSpace(item.Description) ? string.Empty : $" — {item.Description}")}"
        : $"{item.Name} está indisponível no momento.";

    private static string AnswerDelivery(ZestContext context, string text)
    {
        if (!context.DeliveryEnabled)
        {
            return context.PickupEnabled
                ? "No momento não fazemos entrega, somente retirada."
                : "No momento não fazemos entrega.";
        }

        if (context.Zones.Count == 0)
        {
            return $"A taxa de entrega é {Money(context.FlatDeliveryFeeInCents)}.";
        }

        var zone = context.Zones.FirstOrDefault(item => Has(text, Normalize(item.District)));

        return zone is not null
            ? $"Entregamos em {zone.District}. A taxa é {Money(zone.FeeInCents)}."
            : $"Entregamos em: {string.Join(", ", context.Zones.Take(15).Select(item => $"{item.District} ({Money(item.FeeInCents)})"))}.";
    }

    private static List<string> Tokens(string name) => Normalize(name)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length >= 3 && !StopWords.Contains(token))
        .ToList();

    private static string Money(long cents) => $"R$ {(cents / 100m).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}";
}
