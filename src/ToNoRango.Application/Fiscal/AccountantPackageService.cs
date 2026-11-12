using System.Globalization;
using System.IO.Compression;
using System.Text;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Fiscal;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

public sealed record AccountantPackageRequest(int Year, int Month, bool Sintegra = true, bool Sped = true);

public sealed record AccountantPackageDto(string FileName, string ContentBase64, int IssuedNotes, int ReceivedNotes, IReadOnlyList<string> Warnings);

/// <summary>
/// Fechamento do mês para o contador: um .zip com os XMLs emitidos e recebidos, SINTEGRA e SPED gerados
/// e um resumo em CSV. O contador confere, homologa e transmite; nada é transmitido daqui.
/// </summary>
public sealed class AccountantPackageService(
    IRepository<FiscalDocument> documents,
    IRepository<ReceivedNote> received,
    FiscalService fiscal)
{
    public Task<Result<AccountantPackageDto>> BuildAsync(Guid establishmentId, IReadOnlyCollection<string> modules, AccountantPackageRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantPackageDto>(async () =>
        {
            if (request.Month is < 1 or > 12 || request.Year is < 2020 or > 2100)
            {
                return Result<AccountantPackageDto>.Failure(AppErrors.Validation("Competência inválida."));
            }

            var start = new DateTimeOffset(request.Year, request.Month, 1, 0, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
            var end = start.AddMonths(1);
            var issued = (await documents.ListAsync(item => item.EstablishmentId == establishmentId && !item.IsSimulation && item.CreatedAt >= start && item.CreatedAt < end, item => item.Number, cancellationToken, take: 100_000))
                .Where(item => item.Xml is not null && item.Status is FiscalDocumentStatus.Authorized or FiscalDocumentStatus.Cancelled)
                .ToList();
            var incoming = (await received.ListAsync(item => item.EstablishmentId == establishmentId && item.IssuedAt >= start && item.IssuedAt < end, item => item.IssuedAt, cancellationToken, take: 100_000))
                .ToList();
            var warnings = new List<string>();
            var period = $"{request.Year:0000}-{request.Month:00}";

            using var buffer = new MemoryStream();

            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var note in issued)
                {
                    var folder = note.Model == FiscalDocument.NfeModel ? "emitidas/nfe" : "emitidas/nfce";
                    var state = note.Status == FiscalDocumentStatus.Cancelled ? "-cancelada" : string.Empty;
                    await Write(zip, $"{folder}/{note.AccessKey ?? note.Number.ToString(CultureInfo.InvariantCulture)}{state}.xml", note.Xml!);
                }

                foreach (var note in incoming)
                {
                    if (note.Xml is null)
                    {
                        warnings.Add($"Nota recebida {note.AccessKey} ainda sem XML completo (só resumo).");
                        continue;
                    }

                    await Write(zip, $"recebidas/{note.AccessKey}.xml", note.Xml);
                }

                var files = new List<(FiscalFileKind Kind, string Module)>();

                if (request.Sintegra && modules.Contains("fiscal_sintegra"))
                {
                    files.Add((FiscalFileKind.Sintegra, "fiscal_sintegra"));
                }

                if (request.Sped && modules.Contains("fiscal_sped"))
                {
                    files.Add((FiscalFileKind.EfdIcmsIpi, "fiscal_sped"));
                    files.Add((FiscalFileKind.EfdContribuicoes, "fiscal_sped"));
                }

                foreach (var (kind, _) in files)
                {
                    var file = await fiscal.GenerateFileAsync(establishmentId, kind, new FiscalFileRequest(request.Year, request.Month), cancellationToken);

                    if (!file.IsSuccess)
                    {
                        warnings.Add($"{kind}: {file.Error.Description}");
                        continue;
                    }

                    var entry = zip.CreateEntry($"arquivos/{file.Value.FileName}", CompressionLevel.Optimal);
                    await using (var stream = entry.Open())
                    {
                        var bytes = Convert.FromBase64String(file.Value.ContentBase64);
                        await stream.WriteAsync(bytes, cancellationToken);
                    }

                    warnings.AddRange(file.Value.Warnings.Select(warning => $"{kind}: {warning}"));
                }

                await Write(zip, "resumo.csv", Summary(issued, incoming));
                await Write(zip, "LEIA-ME.txt",
                    $"Fechamento fiscal {period}\r\n\r\nNotas emitidas: {issued.Count}\r\nNotas recebidas: {incoming.Count}\r\n\r\n" +
                    "Arquivos gerados para conferência do escritório contábil. A geração não significa transmissão nem validação pela SEFAZ ou Receita.\r\n" +
                    "Valide no PVA/validador oficial antes de transmitir.\r\n\r\nAvisos:\r\n" + string.Join("\r\n", warnings.DefaultIfEmpty("nenhum")));
            }

            return new AccountantPackageDto($"fechamento-fiscal-{period}.zip", Convert.ToBase64String(buffer.ToArray()), issued.Count, incoming.Count, warnings);
        });

    private static string Summary(IReadOnlyList<FiscalDocument> issued, IReadOnlyList<ReceivedNote> incoming)
    {
        var csv = new StringBuilder("tipo;modelo;numero;chave;data;participante;situacao;valor\r\n");

        foreach (var note in issued)
        {
            csv.Append(CultureInfo.InvariantCulture, $"saida;{note.Model};{note.Number};{note.AccessKey};{note.CreatedAt:yyyy-MM-dd};{Clean(note.RecipientName)};{(note.Status == FiscalDocumentStatus.Cancelled ? "cancelada" : "autorizada")};{note.TotalInCents / 100m:0.00}\r\n");
        }

        foreach (var note in incoming)
        {
            csv.Append(CultureInfo.InvariantCulture, $"entrada;55;;{note.AccessKey};{note.IssuedAt:yyyy-MM-dd};{Clean(note.IssuerName)};{(note.Situation == 3 ? "cancelada" : "autorizada")};{note.TotalInCents / 100m:0.00}\r\n");
        }

        return csv.ToString();
    }

    private static string Clean(string? value) => (value ?? string.Empty).Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');

    private static async Task Write(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes);
    }
}
