using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Fiscal;

namespace ToNoRango.Api.Endpoints;

/// <summary>
/// Records every fiscal operation (anything that is not a read) after it runs: who, what, which note
/// or month, and whether it worked. It sits on the route groups, so a new fiscal route is audited too.
/// Secrets (certificate, password, CSC) are never written.
/// </summary>
internal static class FiscalAudit
{
    private static readonly (string Suffix, string Method, string Category, string Action)[] Labels =
    [
        ("/nfce", "POST", "emissao", "Emitiu NFC-e"),
        ("/nfe", "POST", "emissao", "Emitiu NF-e"),
        ("/manual-notes", "POST", "emissao", "Emitiu NF-e avulsa"),
        ("/contingency/send", "POST", "emissao", "Enviou notas da contingência"),
        ("/cancel", "POST", "cancelamento", "Cancelou nota"),
        ("/correction", "POST", "correcao", "Emitiu carta de correção"),
        ("/number-gaps", "POST", "inutilizacao", "Inutilizou numeração"),
        ("/certificate", "POST", "certificado", "Enviou certificado A1"),
        ("/certificate", "DELETE", "certificado", "Removeu certificado A1"),
        ("/fiscal/dfe", "PUT", "configuracao", "Alterou ambiente, CSC ou série"),
        ("/fiscal/profile", "PUT", "configuracao", "Alterou dados do emitente"),
        ("/fiscal/products/{menuItemId:guid}", "PUT", "configuracao", "Alterou tributação de produto"),
        ("/received/sync", "POST", "recebidas", "Buscou notas recebidas na SEFAZ"),
        ("/imported", "POST", "recebidas", "Marcou nota recebida como lançada"),
        ("/files/sintegra", "POST", FiscalAuditEntry.FileCategory, "SINTEGRA"),
        ("/files/efd-icms-ipi", "POST", FiscalAuditEntry.FileCategory, "EFD ICMS/IPI"),
        ("/files/efd-contribuicoes", "POST", FiscalAuditEntry.FileCategory, "EFD Contribuições"),
        ("/accountant-package", "POST", FiscalAuditEntry.FileCategory, "Pacote do contador"),
    ];

    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (HttpMethods.IsGet(http.Request.Method) || Describe(http) is not { } label
            || !Guid.TryParse(http.Request.RouteValues["establishmentId"]?.ToString(), out var establishmentId))
        {
            return await next(context);
        }

        object? result = null;
        var status = StatusCodes.Status500InternalServerError;

        try
        {
            result = await next(context);
            status = result is IStatusCodeHttpResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;
            return result;
        }
        finally
        {
            await WriteAsync(http, establishmentId, label, context.Arguments, status);
        }
    }

    private static (string Category, string Action)? Describe(HttpContext http)
    {
        var pattern = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;

        foreach (var (suffix, method, category, action) in Labels)
        {
            if (pattern.EndsWith(suffix, StringComparison.Ordinal) && string.Equals(http.Request.Method, method, StringComparison.OrdinalIgnoreCase))
            {
                return (category, action);
            }
        }

        return null;
    }

    private static async Task WriteAsync(HttpContext http, Guid establishmentId, (string Category, string Action) label, IList<object?> arguments, int status)
    {
        try
        {
            // Own scope: the request's unit of work may hold changes of a failed operation that must not be saved.
            await using var scope = http.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<FiscalAuditService>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            var values = http.Request.RouteValues;
            var target = new[] { "documentId", "orderId", "noteId", "menuItemId" }.Select(key => values[key]?.ToString()).FirstOrDefault(value => value is not null);

            await service.RecordAsync(
                establishmentId,
                http.Actor(),
                label.Category,
                label.Action,
                target,
                PeriodOf(arguments),
                DetailsOf(arguments),
                status is >= 200 and < 300,
                status,
                clock.GetUtcNow(),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(FiscalAudit)).LogError(exception, "Fiscal audit entry was not written");
        }
    }

    private static string? PeriodOf(IList<object?> arguments)
    {
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case FiscalFileRequest file:
                    return $"{file.Year:0000}-{file.Month:00}";
                case AccountantPackageRequest package:
                    return $"{package.Year:0000}-{package.Month:00}";
            }
        }

        return null;
    }

    private static string? DetailsOf(IList<object?> arguments)
    {
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case CancelNoteRequest cancel:
                    return $"Justificativa: {cancel.Reason}";
                case CorrectionRequest correction:
                    return $"Correção: {correction.Text}";
                case NumberGapRequest gap:
                    return $"Modelo {gap.Model}, série {gap.Series}, números {gap.FromNumber} a {gap.ToNumber}. Justificativa: {gap.Reason}";
                case DfeSettingsRequest settings:
                    return $"Ambiente {(settings.Environment == 1 ? "produção" : "homologação")}, série NF-e {settings.NfeSeries}, próximo número {settings.NfeNextNumber}";
                case AccountantPackageRequest package:
                    return $"SINTEGRA {(package.Sintegra ? "sim" : "não")}, SPED {(package.Sped ? "sim" : "não")}";
            }
        }

        return null;
    }
}
