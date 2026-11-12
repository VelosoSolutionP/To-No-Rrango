using System.Collections.Concurrent;
using System.Threading.Channels;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Fiscal;
using ToNoRango.Application.Media;
using ToNoRango.Domain.Catalog;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Catalog;

/// <summary>A packaged product found by its barcode in the external GTIN catalog.</summary>
public sealed record GtinProduct(string Code, string Name, string? Brand, string? Ncm, string? Cest, byte[]? Image);

public enum GtinLookupStatus
{
    Found,
    NotFound,
    Unavailable,
}

public sealed record GtinLookupResult(GtinLookupStatus Status, GtinProduct? Product, string? Message)
{
    public static GtinLookupResult Missing(string? message = null) => new(GtinLookupStatus.NotFound, null, message ?? "Código não encontrado na base.");

    public static GtinLookupResult Down(string message) => new(GtinLookupStatus.Unavailable, null, message);
}

/// <summary>Barcode catalog. The provider limits calls per minute, so implementations queue every call.</summary>
public interface IGtinCatalog
{
    bool IsConfigured { get; }

    /// <summary>Minimum wait between two calls to the provider.</summary>
    TimeSpan Interval { get; }

    Task<GtinLookupResult> LookupAsync(string code, bool withImage, CancellationToken cancellationToken);
}

public static class GtinCode
{
    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>GS1 check digit for GTIN-8, 12, 13 and 14. Wrong codes never reach the provider.</summary>
    public static bool IsValid(string? value)
    {
        var digits = Digits(value);

        if (digits.Length is not (8 or 12 or 13 or 14) || digits.All(digit => digit == '0'))
        {
            return false;
        }

        var sum = 0;
        for (var index = digits.Length - 2; index >= 0; index--)
        {
            var weight = (digits.Length - 2 - index) % 2 == 0 ? 3 : 1;
            sum += (digits[index] - '0') * weight;
        }

        return (10 - sum % 10) % 10 == digits[^1] - '0';
    }
}

public sealed record GtinLookupDto(string Code, string Name, string? Brand, string? Ncm, string? Cest, string? ImageUrl, Guid? ExistingItemId, string? ExistingItemName);

/// <summary>Taxes applied to every imported product; NCM and CEST come from the catalog.</summary>
public sealed record GtinImportFiscal(string Cfop, string IcmsCst, decimal IcmsRate, string PisCofinsCst, decimal PisRate, decimal CofinsRate);

public sealed record GtinImportRequest(Guid CategoryId, IReadOnlyList<string> Codes, bool WithImages, GtinImportFiscal? Fiscal);

public sealed record GtinImportLine(string Code, string Status, string? Name, string? Message);

public sealed record GtinImportDto(
    Guid Id,
    string State,
    int Total,
    int Done,
    int Created,
    int NotFound,
    int Skipped,
    int Invalid,
    int Failed,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int SecondsLeft,
    IReadOnlyList<GtinImportLine> Lines);

/// <summary>One bulk import. Lives in memory: a restart stops it and the products already created stay.</summary>
public sealed class GtinImportJob(Guid establishmentId, GtinImportRequest request, IReadOnlyList<string> codes, string mediaBaseUrl, DateTimeOffset startedAt)
{
    private readonly object _gate = new();
    private readonly List<GtinImportLine> _lines = [];

    public Guid Id { get; } = Guid.NewGuid();
    public Guid EstablishmentId { get; } = establishmentId;
    public GtinImportRequest Request { get; } = request;
    public IReadOnlyList<string> Codes { get; } = codes;
    public string MediaBaseUrl { get; } = mediaBaseUrl;
    public DateTimeOffset StartedAt { get; } = startedAt;
    public DateTimeOffset? FinishedAt { get; private set; }
    public string State { get; private set; } = "queued";
    public bool CancelRequested { get; private set; }

    public bool IsRunning => State is "queued" or "running";

    public void Begin()
    {
        lock (_gate) { State = "running"; }
    }

    public void Cancel()
    {
        lock (_gate) { CancelRequested = true; }
    }

    public void Add(GtinImportLine line)
    {
        lock (_gate) { _lines.Add(line); }
    }

    public void Finish(DateTimeOffset now)
    {
        lock (_gate)
        {
            State = CancelRequested ? "cancelled" : "done";
            FinishedAt = now;
        }
    }

    public GtinImportDto ToDto(TimeSpan interval)
    {
        lock (_gate)
        {
            var left = Codes.Count - _lines.Count;
            var callsPerCode = Request.WithImages ? 2 : 1;
            var seconds = IsRunning ? (int)Math.Ceiling(left * callsPerCode * interval.TotalSeconds) : 0;

            return new GtinImportDto(
                Id,
                State,
                Codes.Count,
                _lines.Count,
                _lines.Count(line => line.Status == "created"),
                _lines.Count(line => line.Status == "not_found"),
                _lines.Count(line => line.Status == "skipped"),
                _lines.Count(line => line.Status == "invalid"),
                _lines.Count(line => line.Status == "failed"),
                StartedAt,
                FinishedAt,
                seconds,
                _lines.AsEnumerable().Reverse().Take(200).ToList());
        }
    }
}

/// <summary>Keeps the latest import per establishment and hands jobs to the background worker one at a time.</summary>
public sealed class GtinImportQueue
{
    private readonly ConcurrentDictionary<Guid, GtinImportJob> _latest = new();
    private readonly Channel<GtinImportJob> _channel = Channel.CreateUnbounded<GtinImportJob>();

    public ChannelReader<GtinImportJob> Reader => _channel.Reader;

    public GtinImportJob? Latest(Guid establishmentId) => _latest.TryGetValue(establishmentId, out var job) ? job : null;

    public bool TryEnqueue(GtinImportJob job)
    {
        if (Latest(job.EstablishmentId) is { IsRunning: true })
        {
            return false;
        }

        _latest[job.EstablishmentId] = job;
        return _channel.Writer.TryWrite(job);
    }
}

public sealed class GtinService(
    IGtinCatalog catalog,
    GtinImportQueue queue,
    IRepository<MenuItem> menuItems,
    IRepository<MenuCategory> categories,
    MenuService menu,
    MediaService media,
    FiscalService fiscal,
    IClock clock)
{
    public const int MaxCodesPerImport = 500;

    private static readonly Error NotConfigured = Error.External(
        "gtin.not_configured",
        "A consulta por código de barras ainda não foi configurada no servidor.");

    public async Task<Result<GtinLookupDto>> LookupAsync(Guid establishmentId, string code, string mediaBaseUrl, CancellationToken cancellationToken)
    {
        if (!catalog.IsConfigured)
        {
            return Result<GtinLookupDto>.Failure(NotConfigured);
        }

        var digits = GtinCode.Digits(code);

        if (!GtinCode.IsValid(digits))
        {
            return Result<GtinLookupDto>.Failure(AppErrors.Validation("Código de barras inválido: confira os números (o último dígito não confere)."));
        }

        var existing = await menuItems.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId && item.Barcode == digits, cancellationToken);
        var found = await catalog.LookupAsync(digits, withImage: true, cancellationToken);

        if (found.Status == GtinLookupStatus.NotFound)
        {
            return Result<GtinLookupDto>.Failure(AppErrors.NotFound("gtin.not_found", "Este código não está na base de produtos. Preencha à mão."));
        }

        if (found.Status == GtinLookupStatus.Unavailable || found.Product is null)
        {
            return Result<GtinLookupDto>.Failure(Error.External("gtin.unavailable", found.Message ?? "A base de produtos não respondeu. Tente de novo em instantes."));
        }

        var imageUrl = await StoreImageAsync(establishmentId, found.Product.Image, mediaBaseUrl, cancellationToken);
        var product = found.Product;

        return new GtinLookupDto(product.Code, product.Name, product.Brand, product.Ncm, product.Cest, imageUrl, existing?.Id, existing?.Name);
    }

    public async Task<Result<GtinImportDto>> StartImportAsync(Guid establishmentId, GtinImportRequest request, string mediaBaseUrl, CancellationToken cancellationToken)
    {
        if (!catalog.IsConfigured)
        {
            return Result<GtinImportDto>.Failure(NotConfigured);
        }

        var category = await categories.FirstOrDefaultAsync(item => item.Id == request.CategoryId && item.EstablishmentId == establishmentId, cancellationToken);

        if (category is null)
        {
            return Result<GtinImportDto>.Failure(AppErrors.NotFound("category.not_found", "Escolha a categoria em que os produtos vão entrar."));
        }

        var codes = (request.Codes ?? []).Select(GtinCode.Digits).Where(code => code.Length > 0).Distinct().ToList();

        if (codes.Count == 0)
        {
            return Result<GtinImportDto>.Failure(AppErrors.Validation("Cole pelo menos um código de barras."));
        }

        if (codes.Count > MaxCodesPerImport)
        {
            return Result<GtinImportDto>.Failure(AppErrors.Validation($"Importe até {MaxCodesPerImport} códigos por vez."));
        }

        var job = new GtinImportJob(establishmentId, request, codes, mediaBaseUrl, clock.UtcNow);

        if (!queue.TryEnqueue(job))
        {
            return Result<GtinImportDto>.Failure(AppErrors.Conflict("gtin.import_running", "Já existe uma importação em andamento nesta loja. Aguarde ou cancele."));
        }

        return job.ToDto(catalog.Interval);
    }

    public GtinImportDto? GetImport(Guid establishmentId) => queue.Latest(establishmentId)?.ToDto(catalog.Interval);

    public GtinImportDto? CancelImport(Guid establishmentId)
    {
        var job = queue.Latest(establishmentId);
        job?.Cancel();
        return job?.ToDto(catalog.Interval);
    }

    /// <summary>Imports one code of a bulk job: never duplicates a barcode and never shows a product without a price.</summary>
    public async Task<GtinImportLine> ImportOneAsync(GtinImportJob job, string code, CancellationToken cancellationToken)
    {
        if (!GtinCode.IsValid(code))
        {
            return new GtinImportLine(code, "invalid", null, "Dígito verificador não confere; não foi consultado.");
        }

        var existing = await menuItems.FirstOrDefaultAsync(item => item.EstablishmentId == job.EstablishmentId && item.Barcode == code, cancellationToken);

        if (existing is not null)
        {
            return new GtinImportLine(code, "skipped", existing.Name, "Já cadastrado.");
        }

        var found = await catalog.LookupAsync(code, job.Request.WithImages, cancellationToken);

        if (found.Status == GtinLookupStatus.NotFound)
        {
            return new GtinImportLine(code, "not_found", null, found.Message);
        }

        if (found.Status == GtinLookupStatus.Unavailable || found.Product is null)
        {
            return new GtinImportLine(code, "failed", null, found.Message);
        }

        var product = found.Product;
        var imageUrl = await StoreImageAsync(job.EstablishmentId, product.Image, job.MediaBaseUrl, cancellationToken);
        var created = await menu.CreateItemAsync(
            job.EstablishmentId,
            new CreateMenuItemRequest(
                job.Request.CategoryId,
                product.Name,
                product.Brand,
                imageUrl,
                0,
                0,
                IsAvailable: false,
                OptionGroups: [],
                Barcode: code,
                Unit: "UN"),
            cancellationToken);

        if (created.IsFailure)
        {
            return new GtinImportLine(code, "failed", product.Name, created.Error.Description);
        }

        // Without a price it must not reach the customer's menu nor the PDV: it waits inactive.
        await menu.SetItemActiveAsync(created.Value.Id, false, cancellationToken);
        var message = "Criado inativo e sem preço: defina o preço e reative.";

        if (job.Request.Fiscal is { } tax && product.Ncm is { Length: 8 })
        {
            var cest = product.Cest is { Length: 7 } ? product.Cest : null;
            var saved = await fiscal.SaveProductAsync(
                job.EstablishmentId,
                created.Value.Id,
                new ProductFiscalRequest(product.Ncm, tax.Cfop, cest, tax.IcmsCst, tax.IcmsRate, tax.PisCofinsCst, tax.PisRate, tax.CofinsRate),
                cancellationToken);

            message = saved.IsSuccess ? $"{message} NCM {product.Ncm} gravado." : $"{message} Tributação não gravada: {saved.Error.Description}";
        }

        return new GtinImportLine(code, "created", product.Name, message);
    }

    private async Task<string?> StoreImageAsync(Guid establishmentId, byte[]? image, string mediaBaseUrl, CancellationToken cancellationToken)
    {
        if (image is null || image.Length == 0 || image.Length > MediaService.MaxBytes || MediaService.DetectContentType(image) is null)
        {
            return null;
        }

        var uploaded = await media.UploadAsync(establishmentId, image, cancellationToken);
        return uploaded.IsSuccess ? $"{mediaBaseUrl.TrimEnd('/')}/api/public/media/{uploaded.Value.Id}" : null;
    }
}
