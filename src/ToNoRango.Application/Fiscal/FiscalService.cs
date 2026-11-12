using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

public enum FiscalIssueOutcome
{
    Authorized = 1,
    Rejected = 2,
    /// <summary>No answer: the note may or may not have reached the SEFAZ.</summary>
    Unanswered = 3
}

public sealed record FiscalIssueResult(
    FiscalIssueOutcome Outcome,
    string Message,
    string? AccessKey = null,
    string? Protocol = null,
    string? Xml = null);

public interface IFiscalGateway
{
    bool IsConfigured { get; }

    /// <summary>1 = production, 2 = homologation.</summary>
    int Environment { get; }

    Task<FiscalIssueResult> IssueAsync(string ini, CancellationToken cancellationToken);
}

public sealed record FiscalProfileRequest(
    string LegalName,
    string? TradeName,
    string Cnpj,
    string StateRegistration,
    string Street,
    string Number,
    string District,
    string City,
    string State,
    string PostalCode,
    string CityIbgeCode,
    int Series,
    int NextNumber,
    TaxRegime TaxRegime,
    string? AccountantName = null,
    string? AccountantCpf = null,
    string? AccountantCrc = null,
    string? AccountantEmail = null,
    string? SpedProfile = null);

public sealed record ProductFiscalRequest(string Ncm, string Cfop, string? Cest, string IcmsCst, decimal IcmsRate, string PisCofinsCst, decimal PisRate, decimal CofinsRate);

public sealed record FiscalProductDto(Guid MenuItemId, string Name, string? Ncm, string? Cfop, string? Cest, string? IcmsCst, decimal? IcmsRate, string? PisCofinsCst, decimal? PisRate, decimal? CofinsRate);

public sealed record FiscalPendingOrderDto(Guid OrderId, string Code, string CustomerName, long TotalInCents, DateTimeOffset CreatedAt);

public sealed record FiscalDocumentDto(
    Guid Id,
    Guid OrderId,
    string OrderCode,
    int Series,
    int Number,
    int Environment,
    FiscalDocumentStatus Status,
    string? AccessKey,
    string? Protocol,
    string? Message,
    long TotalInCents,
    bool HasXml,
    DateTimeOffset UpdatedAt,
    int Model = FiscalDocument.NfceModel,
    string? RecipientName = null,
    string? CancelReason = null,
    DateTimeOffset? ContingencyAt = null,
    int CorrectionSequence = 0,
    string? CorrectionText = null,
    int Purpose = 1);

public sealed record FiscalOverviewDto(
    bool IsMonitorConfigured,
    int Environment,
    FiscalProfileRequest? Profile,
    IReadOnlyList<FiscalProductDto> Products,
    IReadOnlyList<FiscalPendingOrderDto> PendingOrders,
    IReadOnlyList<FiscalDocumentDto> Documents);

public sealed record FiscalXmlDto(string FileName, string Xml);

public enum FiscalFileKind
{
    Sintegra = 1,
    EfdIcmsIpi = 2,
    EfdContribuicoes = 3
}

public sealed record FiscalFileRequest(int Year, int Month, long PreviousCreditInCents = 0, string? RevenueCode = null, DateOnly? DueDate = null);

public sealed record FiscalFileDto(string FileName, string ContentBase64, int Notes, IReadOnlyList<string> Warnings);

public sealed class FiscalService(
    IEstablishmentRepository establishments,
    IOrderRepository orders,
    IRepository<Order> orderList,
    IRepository<MenuItem> menuItems,
    IRepository<FiscalProfile> profiles,
    IRepository<ProductFiscalData> productData,
    IRepository<FiscalDocument> documents,
    IFiscalGateway gateway,
    DfeService dfe,
    FiscalNoteSource noteSource,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const string EfdIcmsLayoutVersion = "020";

    public Task<Result<FiscalOverviewDto>> GetOverviewAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalOverviewDto>(async () =>
        {
            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);
            var items = await menuItems.ListAsync(item => item.EstablishmentId == establishmentId && item.IsActive, item => item.Name, cancellationToken);
            var taxes = (await productData.ListAsync(item => item.EstablishmentId == establishmentId, item => item.MenuItemId, cancellationToken, take: 2000))
                .ToDictionary(item => item.MenuItemId);
            var notes = await documents.ListAsync(item => item.EstablishmentId == establishmentId && !item.IsSimulation, item => item.CreatedAt, cancellationToken, descending: true, take: 100);
            var paid = await orderList.ListAsync(
                item => item.EstablishmentId == establishmentId && item.PaymentStatus == PaymentStatus.Paid && item.Status != OrderStatus.Cancelled,
                item => item.CreatedAt,
                cancellationToken,
                descending: true,
                take: 100);
            var authorized = notes.Where(note => note.Status == FiscalDocumentStatus.Authorized).Select(note => note.OrderId).ToHashSet();

            return new FiscalOverviewDto(
                gateway.IsConfigured,
                gateway.Environment,
                profile is null || profile.Cnpj.Length == 0 ? null : ToRequest(profile),
                items.Select(item =>
                {
                    taxes.TryGetValue(item.Id, out var tax);
                    return ToProduct(item, tax);
                }).ToList(),
                paid.Where(order => !authorized.Contains(order.Id))
                    .Select(order => new FiscalPendingOrderDto(order.Id, order.Code, order.CustomerName, order.TotalInCents, order.CreatedAt))
                    .ToList(),
                notes.Select(ToDto).ToList());
        });

    public Task<Result<FiscalProfileRequest>> SaveProfileAsync(Guid establishmentId, FiscalProfileRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalProfileRequest>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<FiscalProfileRequest>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (profile is null)
            {
                profile = new FiscalProfile(establishment.TenantId, establishmentId);
                profiles.Add(profile);
            }

            profile.SetRegime(request.TaxRegime);
            profile.SetAccounting(request.AccountantName, request.AccountantCpf, request.AccountantCrc, request.AccountantEmail, request.SpedProfile);
            profile.Update(
                request.LegalName,
                request.TradeName,
                request.Cnpj,
                request.StateRegistration,
                request.Street,
                request.Number,
                request.District,
                request.City,
                request.State,
                request.PostalCode,
                request.CityIbgeCode,
                request.Series,
                request.NextNumber);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToRequest(profile);
        });

    public Task<Result<FiscalProductDto>> SaveProductAsync(Guid establishmentId, Guid menuItemId, ProductFiscalRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalProductDto>(async () =>
        {
            var item = await menuItems.FirstOrDefaultAsync(candidate => candidate.Id == menuItemId && candidate.EstablishmentId == establishmentId, cancellationToken);

            if (item is null)
            {
                return Result<FiscalProductDto>.Failure(AppErrors.NotFound("fiscal.product_not_found", "Produto não encontrado."));
            }

            var tax = await productData.FirstOrDefaultAsync(candidate => candidate.MenuItemId == menuItemId, cancellationToken);

            if (tax is null)
            {
                tax = new ProductFiscalData(item.TenantId, establishmentId, menuItemId);
                productData.Add(tax);
            }

            tax.Update(request.Ncm, request.Cfop, request.Cest, request.IcmsCst, request.IcmsRate, request.PisCofinsCst, request.PisRate, request.CofinsRate);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToProduct(item, tax);
        });

    public async Task<Result<FiscalDocumentDto>> IssueAsync(Guid establishmentId, Guid orderId, CancellationToken cancellationToken)
    {
        // A store with its own A1 certificate talks to the SEFAZ directly; ACBr stays for whoever already uses it.
        var own = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        return own is { HasCertificate: true }
            ? await dfe.IssueAsync(establishmentId, orderId, FiscalDocument.NfceModel, null, cancellationToken)
            : await IssueWithAcbrAsync(establishmentId, orderId, cancellationToken);
    }

    private Task<Result<FiscalDocumentDto>> IssueWithAcbrAsync(Guid establishmentId, Guid orderId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalDocumentDto>(async () =>
        {
            if (!gateway.IsConfigured)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict(
                    "fiscal.monitor_off",
                    "Nenhum emissor fiscal configurado: envie o certificado A1 da loja (ou ligue o ACBrMonitor). Nenhuma nota foi gerada."));
            }

            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<FiscalDocumentDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var order = await orders.GetByIdAsync(orderId, cancellationToken);

            if (order is null || order.EstablishmentId != establishmentId)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.NotFound("fiscal.order_not_found", "Pedido não encontrado."));
            }

            if (order.Status == OrderStatus.Cancelled || order.PaymentStatus != PaymentStatus.Paid)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.order_not_paid", "A NFC-e só é emitida para pedido pago e não cancelado."));
            }

            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (profile is null || profile.Cnpj.Length == 0 || profile.TaxRegime is null)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.profile_missing", "Preencha os dados do emitente antes de emitir."));
            }

            var itemIds = order.Items.Select(item => item.MenuItemId).Distinct().ToList();
            var taxes = (await productData.ListAsync(item => itemIds.Contains(item.MenuItemId), item => item.MenuItemId, cancellationToken))
                .ToDictionary(item => item.MenuItemId);
            var missing = order.Items.Where(item => !taxes.ContainsKey(item.MenuItemId)).Select(item => item.Name).Distinct().ToList();

            if (missing.Count > 0)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict(
                    "fiscal.product_incomplete",
                    $"Preencha a tributação de: {string.Join(", ", missing)}."));
            }

            var now = clock.GetUtcNow();
            var document = await documents.FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);

            if (document is null)
            {
                document = new FiscalDocument(order.TenantId, establishmentId, orderId, order.Code, profile.Series, profile.TakeNumber(), gateway.Environment, order.TotalInCents, now);
                documents.Add(document);
            }
            else if (document.Status == FiscalDocumentStatus.Authorized)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.already_issued", "Este pedido já tem NFC-e autorizada."));
            }
            else
            {
                document.Restart(gateway.Environment, order.TotalInCents, now);
            }

            // Grava o número antes de enviar: se a API cair no meio, a nota não volta com outro número.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var lines = order.Items
                .Select(item => new NfceLine(item.MenuItemId.ToString("N"), item.Name, item.Quantity, item.TotalInCents, taxes[item.MenuItemId]))
                .ToList();
            var zone = TimeZoneInfo.FindSystemTimeZoneById(establishment.TimeZone);
            var ini = NfceIniBuilder.Build(profile, document, order, lines, TimeZoneInfo.ConvertTime(now, zone).DateTime);
            var result = await gateway.IssueAsync(ini, CancellationToken.None);
            var finishedAt = clock.GetUtcNow();

            switch (result.Outcome)
            {
                case FiscalIssueOutcome.Authorized:
                    document.Authorize(result.AccessKey, result.Protocol, result.Xml, result.Message, finishedAt);
                    break;
                case FiscalIssueOutcome.Rejected:
                    document.Reject(result.Message, finishedAt);
                    break;
                default:
                    document.MarkUnanswered(result.Message, finishedAt);
                    break;
            }

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return ToDto(document);
        });

    public Task<Result<FiscalXmlDto>> GetXmlAsync(Guid establishmentId, Guid documentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalXmlDto>(async () =>
        {
            var document = await documents.FirstOrDefaultAsync(item => item.Id == documentId && item.EstablishmentId == establishmentId, cancellationToken);

            if (document?.Xml is null)
            {
                return Result<FiscalXmlDto>.Failure(AppErrors.NotFound("fiscal.xml_not_found", "XML não disponível para esta nota."));
            }

            return new FiscalXmlDto($"{document.AccessKey ?? document.Number.ToString()}-nfce.xml", document.Xml);
        });

    public Task<Result<FiscalFileDto>> GenerateFileAsync(Guid establishmentId, FiscalFileKind kind, FiscalFileRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalFileDto>(async () =>
        {
            if (request.Month is < 1 or > 12 || request.Year is < 2020 or > 2100)
            {
                return Result<FiscalFileDto>.Failure(AppErrors.Validation("Período inválido."));
            }

            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (profile is null || profile.TaxRegime is null)
            {
                return Result<FiscalFileDto>.Failure(AppErrors.Conflict("fiscal.profile_missing", "Preencha os dados do emitente com o regime tributário."));
            }

            if (kind != FiscalFileKind.Sintegra && !profile.HasAccounting)
            {
                return Result<FiscalFileDto>.Failure(AppErrors.Conflict("fiscal.accounting_missing", "Preencha contador e perfil do SPED."));
            }

            if (kind == FiscalFileKind.EfdContribuicoes && profile.TaxRegime == TaxRegime.LucroReal)
            {
                return Result<FiscalFileDto>.Failure(AppErrors.Conflict("fiscal.real_not_ready", "A EFD Contribuições do Lucro Real ainda não está disponível: ela exige apuração de créditos."));
            }

            var start = new DateOnly(request.Year, request.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var warnings = new List<string>();
            var notes = await noteSource.LoadAsync(establishmentId, start, end, warnings, cancellationToken);

            string content;
            string name;

            switch (kind)
            {
                case FiscalFileKind.Sintegra:
                    content = SintegraBuilder.Build(profile, start, end, notes);
                    name = "sintegra";
                    warnings.Add("O SINTEGRA considera só notas modelo 55. NFC-e não entra neste arquivo.");
                    break;
                case FiscalFileKind.EfdIcmsIpi:
                    content = EfdBuilder.BuildIcmsIpi(profile, start, end, notes, new EfdOptions(request.PreviousCreditInCents, request.RevenueCode, request.DueDate, EfdIcmsLayoutVersion), warnings);
                    name = "efd-icms-ipi";
                    break;
                default:
                    content = EfdBuilder.BuildContributions(profile, start, end, notes, warnings);
                    name = "efd-contribuicoes";
                    break;
            }

            return new FiscalFileDto(
                $"{name}-{request.Year}-{request.Month:00}.txt",
                Convert.ToBase64String(System.Text.Encoding.Latin1.GetBytes(content)),
                notes.Count,
                warnings);
        });

    private static FiscalNote? TryRead(string xml, bool isEntry, string? number, List<string> warnings)
    {
        try
        {
            return FiscalNoteReader.Read(xml, isEntry);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            warnings.Add($"A nota {number ?? "sem número"} tem XML ilegível e ficou fora do arquivo.");
            return null;
        }
    }

    private static FiscalProductDto ToProduct(MenuItem item, ProductFiscalData? tax) =>
        new(item.Id, item.Name, tax?.Ncm, tax?.Cfop, tax?.Cest, tax?.IcmsCst, tax?.IcmsRate, tax?.PisCofinsCst, tax?.PisRate, tax?.CofinsRate);

    private static FiscalProfileRequest ToRequest(FiscalProfile profile) => new(
        profile.LegalName,
        profile.TradeName,
        profile.Cnpj,
        profile.StateRegistration,
        profile.Street,
        profile.Number,
        profile.District,
        profile.City,
        profile.State,
        profile.PostalCode,
        profile.CityIbgeCode,
        profile.Series,
        profile.NextNumber,
        profile.TaxRegime ?? TaxRegime.LucroPresumido,
        profile.AccountantName,
        profile.AccountantCpf,
        profile.AccountantCrc,
        profile.AccountantEmail,
        profile.SpedProfile);

    internal static FiscalDocumentDto ToDto(FiscalDocument document) => new(
        document.Id,
        document.OrderId,
        document.OrderCode,
        document.Series,
        document.Number,
        document.Environment,
        document.Status,
        document.AccessKey,
        document.Protocol,
        document.Message,
        document.TotalInCents,
        !string.IsNullOrEmpty(document.Xml),
        document.UpdatedAt,
        document.Model,
        document.RecipientName,
        document.CancelReason,
        document.ContingencyAt,
        document.CorrectionSequence,
        document.CorrectionText,
        document.Purpose);
}
