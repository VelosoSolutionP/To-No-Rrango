using System.Text.RegularExpressions;

namespace ToNoRango.Application.Media;

/// <summary>One real photo found for a product (a photo, never an illustration or a vector).</summary>
public sealed record PhotoOption(string Id, string PreviewUrl, string ImageUrl, string Tags, string Author, string PageUrl);

public sealed record PhotoSearchResult(bool Configured, string Query, IReadOnlyList<PhotoOption> Photos, bool Busy = false);

/// <summary>The bank did not answer (or asked to slow down): not the same as finding nothing.</summary>
public sealed class PhotoSearchUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record ImportPhotoRequest(string Url);

/// <summary>Bank of real photos (Pixabay) used to give each product a picture.</summary>
public interface IPhotoSearch
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<PhotoOption>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Downloads a photo found by the search; null when the address is not from the bank.</summary>
    Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken);
}

public static partial class ProductPhotoQuery
{
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "grande", "media", "média", "pequena", "pequeno", "un", "und", "unid", "kit", "fardo", "caixa", "pacote", "com", "de", "do", "da", "e", "anos", "litro", "litros",
    };

    /// <summary>"Cerveja Pilsen Lata 350ml - Fardo 12un" becomes "cerveja pilsen lata": what the photo shows, not how it is sold.</summary>
    public static string Clean(string productName)
    {
        var name = productName.Split(" - ", 2)[0];
        name = Parenthesis().Replace(name, " ");
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !word.Any(char.IsDigit) && !Noise.Contains(word))
            .Select(word => word.Trim(',', '.', '+', '/').ToLowerInvariant())
            .Where(word => word.Length > 1);
        var query = string.Join(' ', words);
        return string.IsNullOrWhiteSpace(query) ? productName.Trim() : query;
    }

    /// <summary>The cleaned name, then shorter versions of it, for when the full one finds nothing.</summary>
    public static IEnumerable<string> Attempts(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var take = words.Length; take >= 1; take--)
        {
            yield return string.Join(' ', words.Take(take));
        }
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesis();
}

public sealed class PhotoSearchService(IPhotoSearch bank)
{
    public async Task<PhotoSearchResult> SearchAsync(string text, CancellationToken cancellationToken)
    {
        var query = ProductPhotoQuery.Clean(text ?? string.Empty);

        if (!bank.IsConfigured || query.Length == 0)
        {
            return new PhotoSearchResult(bank.IsConfigured, query, []);
        }

        foreach (var attempt in ProductPhotoQuery.Attempts(query))
        {
            IReadOnlyList<PhotoOption> photos;

            try
            {
                photos = await bank.SearchAsync(attempt, cancellationToken);
            }
            catch (PhotoSearchUnavailableException)
            {
                return new PhotoSearchResult(true, attempt, [], Busy: true);
            }

            if (photos.Count > 0)
            {
                return new PhotoSearchResult(true, attempt, photos);
            }
        }

        return new PhotoSearchResult(true, query, []);
    }
}
