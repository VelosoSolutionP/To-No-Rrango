using System.Globalization;
using System.Xml.Linq;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Fiscal;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

public sealed record ReceivedNoteDto(
    Guid Id,
    string AccessKey,
    string IssuerDocument,
    string IssuerName,
    DateTimeOffset IssuedAt,
    long TotalInCents,
    int Situation,
    ReceivedNoteStatus Status,
    bool HasXml,
    DateTimeOffset? AcknowledgedAt,
    string? LastEvent);

public sealed record ReceivedSyncDto(int Downloaded, int Acknowledged, string Message, DateTimeOffset? NextAllowedAt, IReadOnlyList<ReceivedNoteDto> Notes);

/// <summary>
/// Baixa da SEFAZ todas as NF-e emitidas contra o CNPJ da loja, só com o certificado A1
/// (distribuição DF-e). Resumo recebido ganha ciência da operação para o XML completo vir na próxima busca.
/// </summary>
public sealed class ReceivedNotesService(
    IRepository<FiscalProfile> profiles,
    IRepository<ReceivedNote> notes,
    IDfeEmitter emitter,
    ISecretProtector protector,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const int MaxCallsPerSync = 20;
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    /// <summary>Notes added in this sync and not saved yet: the same key may come as summary and full XML in one batch.</summary>
    private readonly Dictionary<string, ReceivedNote> added = new(StringComparer.Ordinal);

    public Task<Result<IReadOnlyList<ReceivedNoteDto>>> ListAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<IReadOnlyList<ReceivedNoteDto>>(async () =>
            (await notes.ListAsync(item => item.EstablishmentId == establishmentId, item => item.IssuedAt, cancellationToken, take: 5000))
                .OrderByDescending(item => item.IssuedAt).Select(ToDto).ToList());

    public Task<Result<FiscalXmlDto>> GetXmlAsync(Guid establishmentId, Guid noteId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalXmlDto>(async () =>
        {
            var note = await notes.FirstOrDefaultAsync(item => item.Id == noteId && item.EstablishmentId == establishmentId, cancellationToken);
            return note?.Xml is null
                ? Result<FiscalXmlDto>.Failure(AppErrors.NotFound("fiscal.received_without_xml", "O XML completo ainda não chegou. Busque de novo em uma hora."))
                : new FiscalXmlDto($"{note.AccessKey}-nfe.xml", note.Xml);
        });

    public Task<Result<ReceivedSyncDto>> SyncAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ReceivedSyncDto>(async () =>
        {
            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (profile is null || !profile.HasCertificate)
            {
                return Result<ReceivedSyncDto>.Failure(AppErrors.Conflict("fiscal.no_certificate", "Envie o certificado A1 da loja: é com ele que a SEFAZ entrega as notas do seu CNPJ."));
            }

            var now = clock.GetUtcNow();

            if (profile.NextDistributionAt is { } wait && wait > now)
            {
                return Result<ReceivedSyncDto>.Failure(AppErrors.Conflict("fiscal.distribution_wait",
                    $"A SEFAZ só deixa buscar de novo às {wait.ToLocalTime():HH:mm}. Buscar antes bloqueia o CNPJ por uma hora."));
            }

            var credentials = new DfeCredentials(
                Convert.FromBase64String(protector.Unprotect(profile.EncryptedCertificate!)),
                protector.Unprotect(profile.EncryptedCertificatePassword!),
                null,
                null);

            var downloaded = 0;
            var message = "Nenhuma nota nova.";
            DateTimeOffset? next = null;
            var summaries = new List<ReceivedNote>();

            for (var call = 0; call < MaxCallsPerSync; call++)
            {
                var answer = await emitter.DistributeAsync(profile.Environment, profile.State, profile.Cnpj, profile.LastNsu, credentials, CancellationToken.None);
                message = $"{answer.Status} - {answer.Message}";

                if (answer.Status != 138)
                {
                    // 137 nada novo e 656 consumo indevido: a SEFAZ pede uma hora de espera.
                    next = answer.Status is 137 or 656 ? clock.GetUtcNow().AddHours(1) : null;
                    break;
                }

                foreach (var document in answer.Documents)
                {
                    var note = await ApplyAsync(profile, document, cancellationToken);

                    if (note is not null)
                    {
                        downloaded++;

                        if (note.Status == ReceivedNoteStatus.Summary && note.AcknowledgedAt is null && note.Situation == 1)
                        {
                            summaries.Add(note);
                        }
                    }
                }

                profile.SetDistribution(answer.LastNsu, null);
                await unitOfWork.SaveChangesAsync(CancellationToken.None);

                if (answer.LastNsu >= answer.MaxNsu)
                {
                    next = clock.GetUtcNow().AddHours(1);
                    message = "Tudo baixado até agora.";
                    break;
                }
            }

            var acknowledged = 0;

            foreach (var note in summaries.DistinctBy(item => item.Id))
            {
                var answer = await emitter.AcknowledgeAsync(profile.Environment, profile.State, profile.Cnpj, note.AccessKey, credentials, CancellationToken.None);

                if (answer.Accepted)
                {
                    note.Acknowledge(clock.GetUtcNow());
                    acknowledged++;
                }
            }

            profile.SetDistribution(profile.LastNsu, next);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            var list = (await notes.ListAsync(item => item.EstablishmentId == establishmentId, item => item.IssuedAt, cancellationToken, take: 5000))
                .OrderByDescending(item => item.IssuedAt).Select(ToDto).ToList();
            return new ReceivedSyncDto(downloaded, acknowledged, message, next, list);
        });

    public Task<Result<ReceivedNoteDto>> MarkImportedAsync(Guid establishmentId, Guid noteId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ReceivedNoteDto>(async () =>
        {
            var note = await notes.FirstOrDefaultAsync(item => item.Id == noteId && item.EstablishmentId == establishmentId, cancellationToken);

            if (note is null)
            {
                return Result<ReceivedNoteDto>.Failure(AppErrors.NotFound("fiscal.received_not_found", "Nota não encontrada."));
            }

            note.MarkImported(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(note);
        });

    /// <summary>Reads one distributed document. Returns the note touched, or null for documents that are not ours to keep.</summary>
    internal async Task<ReceivedNote?> ApplyAsync(FiscalProfile profile, DfeDistributedDocument document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.Xml))
        {
            return null;
        }

        XElement root;

        try
        {
            root = XElement.Parse(document.Xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        string? Field(XElement parent, string name) => parent.Descendants(Ns + name).FirstOrDefault()?.Value;

        switch (root.Name.LocalName)
        {
            case "resNFe":
            {
                var key = Field(root, "chNFe") ?? string.Empty;
                var existing = await FindAsync(profile.EstablishmentId, key, cancellationToken);

                if (existing is not null || key.Length != 44)
                {
                    return existing;
                }

                var note = new ReceivedNote(profile.TenantId, profile.EstablishmentId, document.Nsu, key,
                    Field(root, "CNPJ") ?? Field(root, "CPF") ?? string.Empty, Field(root, "xNome") ?? string.Empty,
                    Date(Field(root, "dhEmi")), Cents(Field(root, "vNF")), int.TryParse(Field(root, "cSitNFe"), out var situation) ? situation : 1, null, now);
                notes.Add(note);
                added[note.AccessKey] = note;
                return note;
            }

            case "nfeProc":
            {
                ParsedNfe parsed;

                try
                {
                    parsed = NfeXmlParser.Parse(document.Xml);
                }
                catch (ArgumentException)
                {
                    return null;
                }

                var existing = await FindAsync(profile.EstablishmentId, parsed.AccessKey, cancellationToken);

                if (existing is not null)
                {
                    existing.AttachXml(document.Nsu, document.Xml, now);
                    return existing;
                }

                var info = root.Descendants(Ns + "ide").FirstOrDefault();
                var note = new ReceivedNote(profile.TenantId, profile.EstablishmentId, document.Nsu, parsed.AccessKey, parsed.SupplierCnpj, parsed.SupplierName,
                    Date(info?.Element(Ns + "dhEmi")?.Value), (long)Math.Round(parsed.Total * 100m), 1, document.Xml, now);
                notes.Add(note);
                added[note.AccessKey] = note;
                return note;
            }

            case "resEvento":
            case "procEventoNFe":
            {
                var key = Field(root, "chNFe") ?? string.Empty;
                var existing = await FindAsync(profile.EstablishmentId, key, cancellationToken);
                var type = Field(root, "tpEvento");
                existing?.RecordEvent(Field(root, "xEvento") ?? Field(root, "descEvento") ?? $"Evento {type}", type == "110111", now);
                return null;
            }

            default:
                return null;
        }
    }

    private async Task<ReceivedNote?> FindAsync(Guid establishmentId, string key, CancellationToken cancellationToken) =>
        added.GetValueOrDefault(key) ?? await notes.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId && item.AccessKey == key, cancellationToken);

    private static DateTimeOffset Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTimeOffset.UnixEpoch;

    private static long Cents(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? (long)Math.Round(amount * 100m) : 0;

    internal static ReceivedNoteDto ToDto(ReceivedNote note) => new(
        note.Id, note.AccessKey, note.IssuerDocument, note.IssuerName, note.IssuedAt, note.TotalInCents, note.Situation, note.Status,
        note.Xml is not null, note.AcknowledgedAt, note.LastEvent);
}
