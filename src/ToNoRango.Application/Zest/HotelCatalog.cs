using System.Text;
using System.Text.Json;

namespace ToNoRango.Application.Zest;

public sealed record HotelBot(string Id, string Name, string? Profession, string? Photo, string? Persona, string? FirstMessage, int KnowledgeNodes, string Prompt);

public interface IHotelClient
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<HotelBot>> ListAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>Reads the Hotel dos Bots panel. Any bot fully approved there can be chosen by the establishment.</summary>
public static class HotelCatalog
{
    private const int MaxPromptLength = 6000;

    public static IReadOnlyList<HotelBot> Parse(JsonElement panel)
    {
        if (!panel.TryGetProperty("bots", out var bots) || bots.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return bots.EnumerateArray().Where(IsAvailable).Select(ToBot).ToList();
    }

    private static bool IsAvailable(JsonElement bot) =>
        Text(bot, "estado") == "homologado"
        && bot.TryGetProperty("validacao", out var validation)
        && validation.ValueKind == JsonValueKind.Object
        && validation.TryGetProperty("validado", out var validated)
        && validated.ValueKind == JsonValueKind.True
        && IsEmptyList(bot, "falhas")
        && IsEmptyList(bot, "lacunas");

    private static HotelBot ToBot(JsonElement bot)
    {
        var name = Text(bot, "nome") ?? "Atendente";
        var profile = bot.TryGetProperty("perfil", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
        var persona = Filled(profile, "persona", name);
        var fallback = Filled(profile, "mensagemFallback", name);
        var nodes = bot.TryGetProperty("nos", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(node => Text(node, "estado") == "homologado").ToList()
            : [];

        var prompt = new StringBuilder();
        prompt.AppendLine($"Seu nome é {name}.");

        if (persona is not null)
        {
            prompt.AppendLine(persona);
        }

        if (fallback is not null)
        {
            prompt.AppendLine($"Quando não souber responder, diga: {fallback}");
        }

        if (profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("regrasAdicionais", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in rules.EnumerateArray().Where(rule => rule.ValueKind == JsonValueKind.String))
            {
                prompt.AppendLine($"Regra: {rule.GetString()}");
            }
        }

        foreach (var node in nodes)
        {
            prompt.AppendLine($"{Text(node, "categoria")}: {Text(node, "conteudo")}");
        }

        var text = prompt.ToString();

        return new HotelBot(
            Text(bot, "agenteId") ?? string.Empty,
            name,
            Text(bot, "profissao"),
            profile.ValueKind == JsonValueKind.Object ? Text(profile, "foto") : null,
            persona,
            Filled(profile, "mensagemInicial", name),
            nodes.Count,
            text.Length > MaxPromptLength ? text[..MaxPromptLength] : text);
    }

    private static string? Filled(JsonElement element, string property, string name)
    {
        var value = element.ValueKind == JsonValueKind.Object ? Text(element, property) : null;
        return string.IsNullOrWhiteSpace(value) ? null : value.Replace("{nome}", name, StringComparison.Ordinal);
    }

    private static bool IsEmptyList(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
