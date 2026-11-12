using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Inventory;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

/// <summary>
/// The notes of a period, read from the XML kept by the system: purchases (entries) and authorized
/// production notes (exits). SINTEGRA, SPED, the assessment and the books all read from here, so
/// every report shows the same numbers.
/// </summary>
public sealed class FiscalNoteSource(IRepository<PurchaseEntry> purchases, IRepository<FiscalDocument> documents)
{
    public async Task<List<FiscalNote>> LoadAsync(Guid establishmentId, DateOnly start, DateOnly end, List<string> warnings, CancellationToken cancellationToken)
    {
        var notes = new List<FiscalNote>();

        var bought = await purchases.ListAsync(item => item.EstablishmentId == establishmentId && item.IssuedOn >= start && item.IssuedOn <= end, item => item.IssuedOn, cancellationToken, take: 5000);
        var withoutXml = bought.Count(item => item.NfeXml is null);

        if (withoutXml > 0)
        {
            warnings.Add($"{withoutXml} compra(s) do período sem XML ficaram de fora.");
        }

        notes.AddRange(bought.Where(item => item.NfeXml is not null).Select(item => TryRead(item.NfeXml!, true, item.InvoiceNumber, warnings)).OfType<FiscalNote>());

        var issuedFrom = new DateTimeOffset(start.AddDays(-2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var issuedTo = new DateTimeOffset(end.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var issued = await documents.ListAsync(
            item => item.EstablishmentId == establishmentId && !item.IsSimulation && item.Status == FiscalDocumentStatus.Authorized && item.CreatedAt >= issuedFrom && item.CreatedAt <= issuedTo,
            item => item.Number,
            cancellationToken,
            take: 20000);
        var homologation = issued.Count(item => item.Environment == NfceIniBuilder.HomologationEnvironment);

        if (homologation > 0)
        {
            warnings.Add($"{homologation} nota(s) de homologação ficaram de fora: elas não têm valor fiscal.");
        }

        var production = issued.Where(item => item.Environment != NfceIniBuilder.HomologationEnvironment).ToList();
        var noXml = production.Count(item => string.IsNullOrEmpty(item.Xml));

        if (noXml > 0)
        {
            warnings.Add($"{noXml} nota(s) emitida(s) sem XML guardado ficaram de fora.");
        }

        notes.AddRange(production
            .Where(item => !string.IsNullOrEmpty(item.Xml))
            .Select(item => TryRead(item.Xml!, false, item.Number.ToString(), warnings))
            .OfType<FiscalNote>()
            .Where(note => note.IssuedOn >= start && note.IssuedOn <= end));

        return notes;
    }

    private static FiscalNote? TryRead(string xml, bool isEntry, string? number, List<string> warnings)
    {
        try
        {
            return FiscalNoteReader.Read(xml, isEntry);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            warnings.Add($"A nota {number ?? "sem número"} tem XML ilegível e ficou de fora.");
            return null;
        }
    }
}

public sealed record FiscalPeriodRequest(int Year, int Month, long PreviousIcmsCreditInCents = 0);

/// <summary>One line of the register of entries or exits.</summary>
public sealed record FiscalBookRow(
    DateOnly IssuedOn,
    bool IsEntry,
    int Model,
    string Series,
    int Number,
    string Key,
    string PartyDocument,
    string PartyName,
    string PartyUf,
    string Cfops,
    long TotalInCents,
    long IcmsBaseInCents,
    long IcmsInCents,
    long StInCents,
    long IpiInCents,
    long PisInCents,
    long CofinsInCents);

/// <summary>Consolidation by CFOP, CST and rate, as the C190 of the SPED.</summary>
public sealed record FiscalCfopTotal(bool IsEntry, string Cfop, string IcmsCst, decimal IcmsRate, long ValueInCents, long IcmsBaseInCents, long IcmsInCents);

public sealed record FiscalBookDto(int Year, int Month, IReadOnlyList<FiscalBookRow> Rows, IReadOnlyList<FiscalCfopTotal> ByCfop, IReadOnlyList<string> Warnings);

public sealed record FiscalTaxLine(string Tax, string Label, long DebitsInCents, long CreditsInCents, long BalanceInCents, string Basis);

public sealed record FiscalAssessmentDto(
    int Year,
    int Month,
    string Regime,
    long ExitsInCents,
    long EntriesInCents,
    int ExitNotes,
    int EntryNotes,
    IReadOnlyList<FiscalTaxLine> Taxes,
    IReadOnlyList<FiscalCfopTotal> ByCfop,
    IReadOnlyList<string> Warnings);

public sealed record FiscalPeriodSummary(int Year, int Month, int ExitNotes, int EntryNotes, long ExitsInCents, long EntriesInCents, int FilesGenerated, DateTimeOffset? LastFileAt, IReadOnlyList<string> Files);

/// <summary>
/// Register of entries and exits, consolidation by CFOP and the monthly tax assessment.
/// Everything here is a conference tool: the numbers come from the XML and must be reviewed by the accountant.
/// </summary>
public sealed class FiscalBooksService(FiscalNoteSource source, IRepository<FiscalProfile> profiles, IRepository<FiscalAuditEntry> audit)
{
    // Lucro Presumido for trade: IRPJ on 8% of revenue at 15% (+10% above R$ 20 mil/month), CSLL on 12% at 9%.
    private const decimal IrpjPresumption = 0.08m;
    private const decimal CsllPresumption = 0.12m;
    private const long IrpjAdditionalThresholdInCents = 2_000_000;

    // Lucro Real non-cumulative PIS/COFINS rates used to estimate credits on purchases.
    private const decimal PisNonCumulative = 0.0165m;
    private const decimal CofinsNonCumulative = 0.076m;

    public Task<Result<FiscalBookDto>> GetBookAsync(Guid establishmentId, FiscalPeriodRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalBookDto>(async () =>
        {
            if (Period(request) is not var (start, end))
            {
                return Result<FiscalBookDto>.Failure(AppErrors.Validation("Período inválido."));
            }

            var warnings = new List<string>();
            var notes = await source.LoadAsync(establishmentId, start, end, warnings, cancellationToken);
            var rows = notes
                .OrderBy(note => note.IsEntry ? 0 : 1)
                .ThenBy(note => note.IssuedOn)
                .ThenBy(note => note.Number)
                .Select(ToRow)
                .ToList();

            return new FiscalBookDto(request.Year, request.Month, rows, ByCfop(notes), warnings);
        });

    public Task<Result<FiscalAssessmentDto>> GetAssessmentAsync(Guid establishmentId, FiscalPeriodRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalAssessmentDto>(async () =>
        {
            if (Period(request) is not var (start, end))
            {
                return Result<FiscalAssessmentDto>.Failure(AppErrors.Validation("Período inválido."));
            }

            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (profile?.TaxRegime is not { } regime)
            {
                return Result<FiscalAssessmentDto>.Failure(AppErrors.Conflict("fiscal.profile_missing", "Preencha os dados do emitente com o regime tributário."));
            }

            var warnings = new List<string>();
            var notes = await source.LoadAsync(establishmentId, start, end, warnings, cancellationToken);
            var exits = notes.Where(note => !note.IsEntry).ToList();
            var entries = notes.Where(note => note.IsEntry).ToList();
            var revenue = exits.Sum(note => note.Total);
            var bought = entries.Sum(note => note.Total);
            var taxes = new List<FiscalTaxLine>();

            var icmsDebits = exits.Sum(note => note.Icms);
            var icmsCredits = entries.Sum(note => note.Icms) + Math.Max(0, request.PreviousIcmsCreditInCents);
            taxes.Add(new FiscalTaxLine("ICMS", "ICMS próprio", icmsDebits, icmsCredits, icmsDebits - icmsCredits,
                request.PreviousIcmsCreditInCents > 0
                    ? $"Débitos das saídas menos créditos das entradas e o saldo credor anterior de {Money(request.PreviousIcmsCreditInCents)}."
                    : "Débitos destacados nas saídas menos créditos destacados nas entradas."));

            var st = entries.Sum(note => note.St) + exits.Sum(note => note.St);
            if (st > 0)
            {
                taxes.Add(new FiscalTaxLine("ICMS-ST", "ICMS por substituição tributária", exits.Sum(note => note.St), entries.Sum(note => note.St), exits.Sum(note => note.St), "Retido nas saídas; o das entradas já foi pago pelo fornecedor (informativo)."));
            }

            var ipi = entries.Sum(note => note.Ipi);
            if (ipi > 0)
            {
                taxes.Add(new FiscalTaxLine("IPI", "IPI nas compras", 0, ipi, 0, "Informativo: comércio varejista não se credita do IPI; entra no custo."));
            }

            var pisDebits = exits.Sum(note => note.Pis);
            var cofinsDebits = exits.Sum(note => note.Cofins);

            if (regime == TaxRegime.LucroReal)
            {
                var creditBase = entries.Sum(note => note.Products - note.Discount);
                var pisCredits = (long)Math.Round(creditBase * PisNonCumulative, MidpointRounding.AwayFromZero);
                var cofinsCredits = (long)Math.Round(creditBase * CofinsNonCumulative, MidpointRounding.AwayFromZero);
                taxes.Add(new FiscalTaxLine("PIS", "PIS não cumulativo", pisDebits, pisCredits, pisDebits - pisCredits, "Créditos estimados em 1,65% sobre as compras do mês. Confira com o contador quais compras dão crédito."));
                taxes.Add(new FiscalTaxLine("COFINS", "COFINS não cumulativa", cofinsDebits, cofinsCredits, cofinsDebits - cofinsCredits, "Créditos estimados em 7,6% sobre as compras do mês. Confira com o contador quais compras dão crédito."));
                warnings.Add("No Lucro Real, IRPJ e CSLL saem do lucro contábil e não são calculados aqui.");
            }
            else
            {
                taxes.Add(new FiscalTaxLine("PIS", "PIS cumulativo", pisDebits, 0, pisDebits, "Destacado nas saídas (0,65% no Lucro Presumido, sem crédito)."));
                taxes.Add(new FiscalTaxLine("COFINS", "COFINS cumulativa", cofinsDebits, 0, cofinsDebits, "Destacado nas saídas (3% no Lucro Presumido, sem crédito)."));

                var irpjBase = (long)Math.Round(revenue * IrpjPresumption, MidpointRounding.AwayFromZero);
                var irpj = (long)Math.Round(irpjBase * 0.15m, MidpointRounding.AwayFromZero)
                    + (long)Math.Round(Math.Max(0, irpjBase - IrpjAdditionalThresholdInCents) * 0.10m, MidpointRounding.AwayFromZero);
                var csll = (long)Math.Round(revenue * CsllPresumption * 0.09m, MidpointRounding.AwayFromZero);
                taxes.Add(new FiscalTaxLine("IRPJ", "IRPJ (estimativa do mês)", irpj, 0, irpj, "Presunção de 8% sobre o faturamento (comércio), 15% mais 10% acima de R$ 20 mil. O recolhimento é trimestral."));
                taxes.Add(new FiscalTaxLine("CSLL", "CSLL (estimativa do mês)", csll, 0, csll, "Presunção de 12% sobre o faturamento (comércio), 9%. O recolhimento é trimestral."));
            }

            if (exits.Any(note => note.Model == FiscalDocument.NfceModel) && exits.All(note => note.Icms == 0))
            {
                warnings.Add("As saídas do mês não destacaram ICMS: confira o CST dos produtos.");
            }

            warnings.Add("Apuração para conferência. Os valores definitivos e as guias são responsabilidade do contador.");

            return new FiscalAssessmentDto(
                request.Year,
                request.Month,
                regime == TaxRegime.LucroReal ? "Lucro Real" : "Lucro Presumido",
                revenue,
                bought,
                exits.Count,
                entries.Count,
                taxes,
                ByCfop(notes),
                warnings);
        });

    /// <summary>The last months: notes in and out, and which files were generated and when.</summary>
    public async Task<IReadOnlyList<FiscalPeriodSummary>> GetPeriodsAsync(Guid establishmentId, int months, DateOnly today, CancellationToken cancellationToken)
    {
        months = Math.Clamp(months, 1, 24);
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var end = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
        var notes = await source.LoadAsync(establishmentId, first, end, [], cancellationToken);
        var files = await audit.ListAsync(
            item => item.EstablishmentId == establishmentId && item.Category == FiscalAuditEntry.FileCategory && item.Succeeded,
            item => item.At,
            cancellationToken,
            descending: true,
            take: 2000);

        var result = new List<FiscalPeriodSummary>();

        for (var month = first; month <= end; month = month.AddMonths(1))
        {
            var period = month.ToString("yyyy-MM");
            var inMonth = notes.Where(note => note.IssuedOn.Year == month.Year && note.IssuedOn.Month == month.Month).ToList();
            var generated = files.Where(item => item.Period == period).ToList();

            result.Add(new FiscalPeriodSummary(
                month.Year,
                month.Month,
                inMonth.Count(note => !note.IsEntry),
                inMonth.Count(note => note.IsEntry),
                inMonth.Where(note => !note.IsEntry).Sum(note => note.Total),
                inMonth.Where(note => note.IsEntry).Sum(note => note.Total),
                generated.Count,
                generated.FirstOrDefault()?.At,
                generated.Select(item => item.Action).Distinct().ToList()));
        }

        result.Reverse();
        return result;
    }

    public async Task<IReadOnlyList<FiscalAuditDto>> GetAuditAsync(Guid establishmentId, int take, CancellationToken cancellationToken)
    {
        var entries = await audit.ListAsync(item => item.EstablishmentId == establishmentId, item => item.At, cancellationToken, descending: true, take: Math.Clamp(take, 1, 1000));
        return entries.Select(item => new FiscalAuditDto(item.Id, item.At, item.Actor, item.Category, item.Action, item.Target, item.Period, item.Details, item.Succeeded, item.StatusCode)).ToList();
    }

    private static (DateOnly Start, DateOnly End)? Period(FiscalPeriodRequest request)
    {
        if (request.Month is < 1 or > 12 || request.Year is < 2020 or > 2100)
        {
            return null;
        }

        var start = new DateOnly(request.Year, request.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    private static FiscalBookRow ToRow(FiscalNote note) => new(
        note.IssuedOn,
        note.IsEntry,
        note.Model,
        note.Series,
        note.Number,
        note.Key,
        note.PartyCnpj,
        note.PartyName,
        note.PartyUf,
        string.Join(", ", note.Lines.Select(line => line.Cfop).Where(cfop => cfop.Length > 0).Distinct().Order()),
        note.Total,
        note.IcmsBase,
        note.Icms,
        note.St,
        note.Ipi,
        note.Pis,
        note.Cofins);

    private static List<FiscalCfopTotal> ByCfop(IEnumerable<FiscalNote> notes) =>
        notes
            .SelectMany(note => note.Lines.Select(line => (note.IsEntry, line)))
            .GroupBy(pair => (pair.IsEntry, pair.line.Cfop, pair.line.IcmsCst, pair.line.IcmsRate))
            .Select(group => new FiscalCfopTotal(
                group.Key.IsEntry,
                group.Key.Cfop,
                group.Key.IcmsCst,
                group.Key.IcmsRate,
                group.Sum(pair => pair.line.Value - pair.line.Discount + pair.line.Extras),
                group.Sum(pair => pair.line.IcmsBase),
                group.Sum(pair => pair.line.Icms)))
            .OrderBy(total => total.IsEntry ? 0 : 1)
            .ThenBy(total => total.Cfop)
            .ToList();

    // The server runs with invariant globalization: Brazilian money is formatted by hand.
    private static string Money(long cents) =>
        "R$ " + (cents / 100m).ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '_').Replace('.', ',').Replace('_', '.');
}

public sealed record FiscalAuditDto(Guid Id, DateTimeOffset At, string Actor, string Category, string Action, string? Target, string? Period, string? Details, bool Succeeded, int StatusCode);

/// <summary>Writes one row per fiscal operation. Failing to write never breaks the operation itself.</summary>
public sealed class FiscalAuditService(IRepository<FiscalAuditEntry> entries, IEstablishmentRepository establishments, IUnitOfWork unitOfWork)
{
    public async Task RecordAsync(Guid establishmentId, string actor, string category, string action, string? target, string? period, string? details, bool succeeded, int statusCode, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return;
        }

        entries.Add(new FiscalAuditEntry(establishment.TenantId, establishmentId, at, actor, category, action, target, period, details, succeeded, statusCode));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
