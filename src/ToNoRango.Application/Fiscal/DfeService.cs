using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

public sealed record UploadCertificateRequest(string PfxBase64, string Password);

public sealed record DfeSettingsRequest(int Environment, string? CscId, string? CscToken, int NfeSeries, int NfeNextNumber);

public sealed record NfeRecipientRequest(
    string Document,
    string Name,
    string? StateRegistration,
    string Street,
    string Number,
    string District,
    string CityIbgeCode,
    string City,
    string State,
    string PostalCode,
    string? Email);

public sealed record CancelNoteRequest(string Reason);

public sealed record CorrectionRequest(string Text);

public sealed record ManualNoteLine(
    string Code,
    string Description,
    decimal Quantity,
    long TotalInCents,
    string Ncm,
    string Cfop,
    string IcmsCst,
    decimal IcmsRate,
    string PisCofinsCst,
    decimal PisRate,
    decimal CofinsRate);

/// <summary>NF-e sem pedido: 2 complementar, 3 ajuste, 4 devolução. A tributação vem da tela, conferida por quem emite.</summary>
public sealed record ManualNoteRequest(
    int Purpose,
    string Nature,
    bool IsEntry,
    string? ReferencedKey,
    NfeRecipientRequest Recipient,
    IReadOnlyList<ManualNoteLine> Lines,
    string? AdditionalInfo);

public sealed record NumberGapRequest(int Model, int Series, int FromNumber, int ToNumber, string Reason);

public sealed record NumberGapDto(Guid Id, int Model, int Series, int FromNumber, int ToNumber, int Environment, string Reason, NumberGapStatus Status, string? Protocol, string? Message, DateTimeOffset CreatedAt)
{
    public static NumberGapDto From(FiscalNumberGap gap) =>
        new(gap.Id, gap.Model, gap.Series, gap.FromNumber, gap.ToNumber, gap.Environment, gap.Reason, gap.Status, gap.Protocol, gap.Message, gap.CreatedAt);
}

public sealed record DfeSettingsDto(
    bool HasCertificate,
    string? CertificateSubject,
    DateTimeOffset? CertificateExpiresAt,
    int Environment,
    string? CscId,
    bool HasCscToken,
    int NfeSeries,
    int NfeNextNumber,
    int ContingencyPending,
    IReadOnlyList<NumberGapDto> Gaps);

/// <summary>
/// NF-e and NFC-e issued straight at the SEFAZ with each store's A1 certificate: issue, offline
/// contingency for NFC-e, cancellation and giving up numbers. The certificate and CSC are stored
/// encrypted and only decrypted for the call.
/// </summary>
public sealed class DfeService(
    IOrderRepository orders,
    IRepository<FiscalProfile> profiles,
    IRepository<ProductFiscalData> productData,
    IRepository<FiscalDocument> documents,
    IRepository<FiscalNumberGap> gaps,
    IDfeEmitter emitter,
    ISecretProtector protector,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const int MaxPfxBytes = 64 * 1024;

    public Task<Result<DfeSettingsDto>> GetSettingsAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<DfeSettingsDto>(async () =>
        {
            var profile = await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);
            var pending = await documents.CountAsync(item => item.EstablishmentId == establishmentId && item.Status == FiscalDocumentStatus.Contingency, cancellationToken);
            var list = await gaps.ListAsync(item => item.EstablishmentId == establishmentId, item => item.CreatedAt, cancellationToken, descending: true, take: 50);

            return new DfeSettingsDto(
                profile?.HasCertificate ?? false,
                profile?.CertificateSubject,
                profile?.CertificateExpiresAt,
                profile?.Environment ?? 2,
                profile?.CscId,
                profile?.EncryptedCscToken is not null,
                profile?.NfeSeries ?? 1,
                profile?.NfeNextNumber ?? 1,
                pending,
                list.Select(NumberGapDto.From).ToList());
        });

    public Task<Result<DfeSettingsDto>> UploadCertificateAsync(Guid establishmentId, UploadCertificateRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<DfeSettingsDto>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            byte[] pfx;

            try
            {
                pfx = Convert.FromBase64String(request.PfxBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                return Result<DfeSettingsDto>.Failure(AppErrors.Validation("Arquivo do certificado inválido."));
            }

            if (pfx.Length is 0 or > MaxPfxBytes)
            {
                return Result<DfeSettingsDto>.Failure(AppErrors.Validation("Envie o arquivo .pfx do certificado A1."));
            }

            (string Subject, string? Cnpj, DateTimeOffset ExpiresAt) read;

            try
            {
                read = emitter.ReadCertificate(pfx, request.Password ?? string.Empty);
            }
            catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or ArgumentException)
            {
                return Result<DfeSettingsDto>.Failure(AppErrors.Validation($"Não consegui abrir o certificado: confira o arquivo e a senha. ({exception.Message})"));
            }

            if (read.ExpiresAt <= clock.GetUtcNow())
            {
                return Result<DfeSettingsDto>.Failure(AppErrors.Validation($"Este certificado venceu em {read.ExpiresAt:dd/MM/yyyy}."));
            }

            // The certificate must belong to the company that issues; the root (first 8 digits) is enough for branches.
            if (read.Cnpj is not null && profile.Cnpj.Length == 14 && read.Cnpj[..8] != profile.Cnpj[..8])
            {
                return Result<DfeSettingsDto>.Failure(AppErrors.Validation($"O certificado é do CNPJ {read.Cnpj}, diferente do emitente {profile.Cnpj}."));
            }

            profile.SetCertificate(protector.Protect(Convert.ToBase64String(pfx)), protector.Protect(request.Password ?? string.Empty), read.Subject, read.ExpiresAt);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return (await GetSettingsAsync(establishmentId, cancellationToken)).Value;
        });

    public Task<Result<DfeSettingsDto>> RemoveCertificateAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<DfeSettingsDto>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            profile.RemoveCertificate();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return (await GetSettingsAsync(establishmentId, cancellationToken)).Value;
        });

    public Task<Result<DfeSettingsDto>> SaveSettingsAsync(Guid establishmentId, DfeSettingsRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<DfeSettingsDto>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            profile.SetEnvironment(request.Environment, request.NfeSeries, request.NfeNextNumber);
            profile.SetCsc(request.CscId, string.IsNullOrWhiteSpace(request.CscToken) ? null : protector.Protect(request.CscToken.Trim()));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return (await GetSettingsAsync(establishmentId, cancellationToken)).Value;
        });

    public Task<Result<DfeEventResult>> StatusAsync(Guid establishmentId, int model, CancellationToken cancellationToken) =>
        Guarded.RunAsync<DfeEventResult>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var credentials = Credentials(profile);
            return await emitter.StatusAsync(model, profile.Environment, profile.State, credentials, cancellationToken);
        });

    /// <summary>
    /// Issues one note for a paid order. NFC-e without answer from the SEFAZ is signed offline
    /// (contingency) so the sale is not held up; it is sent later by <see cref="SendContingencyAsync"/>.
    /// </summary>
    public Task<Result<FiscalDocumentDto>> IssueAsync(Guid establishmentId, Guid orderId, int model, NfeRecipientRequest? recipient, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalDocumentDto>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var order = await orders.GetByIdAsync(orderId, cancellationToken);

            if (order is null || order.EstablishmentId != establishmentId)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.NotFound("fiscal.order_not_found", "Pedido não encontrado."));
            }

            if (order.Status == OrderStatus.Cancelled || order.PaymentStatus != PaymentStatus.Paid)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.order_not_paid", "A nota só é emitida para pedido pago e não cancelado."));
            }

            if (model == FiscalDocument.NfeModel && recipient is null)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Validation("A NF-e precisa dos dados do destinatário (CPF ou CNPJ e endereço)."));
            }

            if (model == FiscalDocument.NfeModel && !IsValidDocument(recipient!.Document))
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Validation("CPF ou CNPJ do destinatário inválido."));
            }

            var itemIds = order.Items.Select(item => item.MenuItemId).Distinct().ToList();
            var taxes = (await productData.ListAsync(item => itemIds.Contains(item.MenuItemId), item => item.MenuItemId, cancellationToken))
                .ToDictionary(item => item.MenuItemId);
            var missing = order.Items.Where(item => !taxes.ContainsKey(item.MenuItemId)).Select(item => item.Name).Distinct().ToList();

            if (missing.Count > 0)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.product_incomplete", $"Preencha a tributação de: {string.Join(", ", missing)}."));
            }

            var existing = await documents.FirstOrDefaultAsync(item => item.OrderId == orderId && item.Model == model, cancellationToken);

            if (existing is { Status: FiscalDocumentStatus.Authorized or FiscalDocumentStatus.Contingency })
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.already_issued", "Este pedido já tem nota deste modelo."));
            }

            var now = clock.GetUtcNow();
            var document = existing;

            if (document is null)
            {
                var number = model == FiscalDocument.NfeModel ? profile.TakeNfeNumber() : profile.TakeNumber();
                var series = model == FiscalDocument.NfeModel ? profile.NfeSeries : profile.Series;
                document = new FiscalDocument(order.TenantId, establishmentId, orderId, order.Code, series, number, profile.Environment, order.TotalInCents, now, model, recipient?.Document, recipient?.Name);
                documents.Add(document);
            }
            else
            {
                document.Restart(profile.Environment, order.TotalInCents, now);
            }

            // The number is saved before sending: if the API falls in the middle, the retry keeps it.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var issue = BuildIssue(profile, document, order, taxes, recipient, now);
            var result = await emitter.AuthorizeAsync(issue, CancellationToken.None);

            if (result.Outcome == DfeOutcome.Unanswered && model == FiscalDocument.NfceModel)
            {
                // NFC-e may be issued offline and sent within 24 hours; NF-e waits for the SEFAZ.
                var offline = emitter.Sign(issue with { OfflineContingency = true, ContingencyAt = now, ContingencyReason = "SEFAZ SEM RESPOSTA NO MOMENTO DA VENDA" });
                document.MarkContingency(offline.AccessKey!, offline.Xml!, $"Emitida em contingência: {result.Message}. Envie em até 24 horas.", clock.GetUtcNow());
            }
            else
            {
                Apply(document, result);
            }

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return FiscalService.ToDto(document);
        });

    /// <summary>Sends every NFC-e signed in contingency. Each one keeps its key; only the protocol is added.</summary>
    public Task<Result<IReadOnlyList<FiscalDocumentDto>>> SendContingencyAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<IReadOnlyList<FiscalDocumentDto>>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var credentials = Credentials(profile);
            var pending = await documents.ListAsync(
                item => item.EstablishmentId == establishmentId && item.Status == FiscalDocumentStatus.Contingency,
                item => item.CreatedAt,
                cancellationToken,
                take: 200);

            foreach (var document in pending)
            {
                var result = await emitter.SendSignedAsync(document.Model, document.Environment, profile.State, document.Xml!, credentials, CancellationToken.None);

                if (result.Outcome != DfeOutcome.Unanswered)
                {
                    Apply(document, result);
                }

                await unitOfWork.SaveChangesAsync(CancellationToken.None);
            }

            return pending.Select(FiscalService.ToDto).ToList();
        });

    public Task<Result<FiscalDocumentDto>> CancelAsync(Guid establishmentId, Guid documentId, CancelNoteRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalDocumentDto>(async () =>
        {
            var reason = (request.Reason ?? string.Empty).Trim();

            if (reason.Length is < 15 or > 255)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Validation("A justificativa do cancelamento precisa ter de 15 a 255 caracteres."));
            }

            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var document = await documents.FirstOrDefaultAsync(item => item.Id == documentId && item.EstablishmentId == establishmentId, cancellationToken);

            if (document is not { Status: FiscalDocumentStatus.Authorized, AccessKey: not null, Protocol: not null })
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.not_cancellable", "Só uma nota autorizada pode ser cancelada."));
            }

            var answer = document.IsSimulation
                ? new DfeEventResult(true, "Simulação.", FiscalDocument.SimulatedProtocol)
                : await emitter.CancelAsync(
                    new DfeCancel(document.Model, document.Environment, profile.State, profile.Cnpj, document.AccessKey, document.Protocol, reason, Credentials(profile)),
                    CancellationToken.None);

            if (!answer.Accepted)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.cancel_refused", $"A SEFAZ não aceitou o cancelamento: {answer.Message}"));
            }

            document.Cancel(answer.Protocol ?? string.Empty, reason, answer.Xml, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return FiscalService.ToDto(document);
        });

    public Task<Result<FiscalDocumentDto>> IssueManualAsync(Guid establishmentId, ManualNoteRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalDocumentDto>(async () =>
        {
            var error = ValidateManual(request);

            if (error is not null)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Validation(error));
            }

            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var key = request.ReferencedKey is null ? null : new string(request.ReferencedKey.Where(char.IsAsciiDigit).ToArray());
            var total = request.Lines.Sum(line => line.TotalInCents);
            var now = clock.GetUtcNow();
            var label = request.Purpose switch { 2 => "COMPL", 3 => "AJUSTE", _ => "DEVOL" };

            // Nota sem pedido: o "pedido" é um identificador próprio, para caber no mesmo registro das notas de venda.
            var document = new FiscalDocument(
                profile.TenantId, establishmentId, Guid.NewGuid(), $"{label}-{now:yyMMddHHmm}", profile.NfeSeries, profile.TakeNfeNumber(),
                profile.Environment, total, now, FiscalDocument.NfeModel, request.Recipient.Document, request.Recipient.Name, request.Purpose, key);
            documents.Add(document);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var recipient = request.Recipient;
            var issue = new DfeIssue(
                FiscalDocument.NfeModel, document.Environment, document.Series, document.Number, document.NumericCode, now,
                new DfeIssuer(profile.Cnpj, profile.LegalName, profile.TradeName, profile.StateRegistration, profile.Street, profile.Number, profile.District, profile.CityIbgeCode, profile.City, profile.State, profile.PostalCode),
                new DfeRecipient(recipient.Document, recipient.Name, recipient.StateRegistration, recipient.Street, recipient.Number, recipient.District, recipient.CityIbgeCode, recipient.City, recipient.State, recipient.PostalCode, recipient.Email),
                request.Lines.Select(line => new DfeLine(line.Code, line.Description, line.Quantity, line.TotalInCents, 0, 0, line.Ncm, line.Cfop, null, line.IcmsCst, line.IcmsRate, line.PisCofinsCst, line.PisRate, line.CofinsRate, true)).ToList(),
                [],
                false,
                request.AdditionalInfo,
                Credentials(profile),
                Purpose: request.Purpose,
                Nature: request.Nature.Trim(),
                ReferencedKeys: key is null ? null : [key],
                IsEntry: request.IsEntry,
                FinalConsumer: false);

            Apply(document, await emitter.AuthorizeAsync(issue, CancellationToken.None));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return FiscalService.ToDto(document);
        });

    public static string? ValidateManual(ManualNoteRequest request)
    {
        if (request.Purpose is < 2 or > 4)
        {
            return "Escolha a finalidade: complementar, ajuste ou devolução.";
        }

        if (string.IsNullOrWhiteSpace(request.Nature) || request.Nature.Trim().Length > 60)
        {
            return "Informe a natureza da operação (até 60 caracteres).";
        }

        var key = new string((request.ReferencedKey ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (request.Purpose is 2 or 4 && key.Length != 44)
        {
            return "Devolução e nota complementar precisam da chave (44 dígitos) da nota de origem.";
        }

        if (key.Length is not (0 or 44))
        {
            return "A chave da nota de origem tem 44 dígitos.";
        }

        if (request.Recipient is null || !IsValidDocument(request.Recipient.Document))
        {
            return "CPF ou CNPJ do destinatário inválido.";
        }

        if (request.Lines is not { Count: > 0 } || request.Lines.Count > 990)
        {
            return "A nota precisa de pelo menos um item.";
        }

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) || line.Quantity <= 0 || line.TotalInCents < 0)
            {
                return "Cada item precisa de descrição, quantidade e valor.";
            }

            if (line.Ncm.Length != 8 || !line.Ncm.All(char.IsAsciiDigit))
            {
                return $"NCM inválido em {line.Description}: são 8 dígitos.";
            }

            if (!FiscalTables.IsCfop(line.Cfop))
            {
                return $"CFOP {line.Cfop} não existe na tabela ({line.Description}).";
            }

            var firstDigit = line.Cfop[0];

            if (request.IsEntry != firstDigit is '1' or '2' or '3')
            {
                return $"CFOP {line.Cfop} não combina com nota de {(request.IsEntry ? "entrada" : "saída")} ({line.Description}).";
            }
        }

        return null;
    }

    /// <summary>
    /// Note asked by a partner system through the fiscal API. The external id makes it idempotent:
    /// the same id never creates a second note, it returns the one already issued. A test key
    /// simulates the authorization without the SEFAZ and without touching the store's numbering.
    /// </summary>
    public Task<Result<FiscalDocumentDto>> IssueExternalAsync(Guid establishmentId, ApiNoteRequest request, bool simulate, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalDocumentDto>(async () =>
        {
            var error = FiscalApiRules.Validate(request);

            if (error is not null)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Validation(error));
            }

            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var orderId = FiscalApiRules.DocumentKey(establishmentId, request.ExternalId, simulate);
            var existing = await documents.FirstOrDefaultAsync(item => item.OrderId == orderId && item.Model == request.Model, cancellationToken);

            if (existing is { Status: FiscalDocumentStatus.Authorized or FiscalDocumentStatus.Contingency or FiscalDocumentStatus.Cancelled })
            {
                return FiscalService.ToDto(existing);
            }

            var total = request.Lines.Sum(line => line.TotalInCents + line.FreightInCents - line.DiscountInCents);
            var now = clock.GetUtcNow();
            var nfe = request.Model == FiscalDocument.NfeModel;
            var key = request.ReferencedKey is null ? null : new string(request.ReferencedKey.Where(char.IsAsciiDigit).ToArray());
            var recipient = request.Recipient;
            var recipientDocument = recipient is null ? null : new string(recipient.Document.Where(char.IsAsciiDigit).ToArray());
            var document = existing;

            if (document is null)
            {
                int series, number;

                if (simulate)
                {
                    // Own series and sequence: the store's real numbering never gets a hole from a test.
                    series = FiscalApiRules.SimulationSeries;
                    number = await documents.CountAsync(item => item.EstablishmentId == establishmentId && item.Model == request.Model && item.IsSimulation, cancellationToken) + 1;
                }
                else
                {
                    series = nfe ? profile.NfeSeries : profile.Series;
                    number = nfe ? profile.TakeNfeNumber() : profile.TakeNumber();
                }

                document = new FiscalDocument(
                    profile.TenantId, establishmentId, orderId, request.ExternalId.Trim(), series, number, simulate ? 2 : profile.Environment, total, now,
                    request.Model, recipientDocument, recipient?.Name.Trim() is { } name ? name[..Math.Min(name.Length, 60)] : null, request.Purpose, key);
                documents.Add(document);
            }
            else
            {
                document.Restart(simulate ? 2 : profile.Environment, total, now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (simulate)
            {
                document.Simulate(FiscalApiRules.SimulatedAccessKey(profile, document, now), clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                return FiscalService.ToDto(document);
            }

            var issue = new DfeIssue(
                request.Model, document.Environment, document.Series, document.Number, document.NumericCode, now,
                new DfeIssuer(profile.Cnpj, profile.LegalName, profile.TradeName, profile.StateRegistration, profile.Street, profile.Number, profile.District, profile.CityIbgeCode, profile.City, profile.State, profile.PostalCode),
                recipient is null
                    ? null
                    : new DfeRecipient(recipientDocument!, recipient.Name, recipient.StateRegistration, recipient.Street, recipient.Number, recipient.District, recipient.CityIbgeCode, recipient.City, recipient.State, recipient.PostalCode, recipient.Email),
                request.Lines.Select(line => new DfeLine(
                    line.Code, line.Description, line.Quantity, line.TotalInCents, line.FreightInCents, line.DiscountInCents,
                    line.Ncm, line.Cfop, string.IsNullOrWhiteSpace(line.Cest) ? null : line.Cest, line.IcmsCst, line.IcmsRate, line.PisCofinsCst, line.PisRate, line.CofinsRate, true)).ToList(),
                (request.Payments ?? []).Select(payment => new DfePayment(FiscalApiRules.PaymentKind(payment.Method), payment.AmountInCents)).ToList(),
                request.IsDelivery,
                request.AdditionalInfo,
                Credentials(profile),
                Purpose: request.Purpose,
                Nature: string.IsNullOrWhiteSpace(request.Nature) ? null : request.Nature.Trim(),
                ReferencedKeys: key is null ? null : [key],
                IsEntry: request.IsEntry,
                FinalConsumer: !nfe || request.FinalConsumer);

            var result = await emitter.AuthorizeAsync(issue, CancellationToken.None);

            if (result.Outcome == DfeOutcome.Unanswered && !nfe)
            {
                var offline = emitter.Sign(issue with { OfflineContingency = true, ContingencyAt = now, ContingencyReason = "SEFAZ SEM RESPOSTA NO MOMENTO DA VENDA" });
                document.MarkContingency(offline.AccessKey!, offline.Xml!, $"Emitida em contingência: {result.Message}. Envie em até 24 horas.", clock.GetUtcNow());
            }
            else
            {
                Apply(document, result);
            }

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return FiscalService.ToDto(document);
        });

    /// <summary>Carta de correção (CC-e) de uma NF-e autorizada. Não corrige valores, impostos, partes nem datas.</summary>
    public Task<Result<FiscalDocumentDto>> CorrectAsync(Guid establishmentId, Guid documentId, CorrectionRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<FiscalDocumentDto>(async () =>
        {
            var text = string.Join(' ', (request.Text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (text.Length is < 15 or > 1000)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Validation("O texto da carta de correção precisa ter de 15 a 1000 caracteres."));
            }

            var profile = await RequireProfileAsync(establishmentId, cancellationToken);
            var document = await documents.FirstOrDefaultAsync(item => item.Id == documentId && item.EstablishmentId == establishmentId, cancellationToken);

            if (document is not { Model: FiscalDocument.NfeModel, Status: FiscalDocumentStatus.Authorized, AccessKey: not null })
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.not_correctable", "Carta de correção só vale para NF-e (modelo 55) autorizada."));
            }

            if (document.CorrectionSequence >= FiscalDocument.MaxCorrections)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.too_many_corrections", "Esta NF-e já tem 20 cartas de correção, o máximo da SEFAZ."));
            }

            var sequence = document.CorrectionSequence + 1;
            var answer = document.IsSimulation
                ? new DfeEventResult(true, "Simulação.", FiscalDocument.SimulatedProtocol)
                : await emitter.CorrectAsync(
                    new DfeCorrection(document.Environment, profile.State, profile.Cnpj, document.AccessKey, sequence, text, Credentials(profile)),
                    CancellationToken.None);

            if (!answer.Accepted)
            {
                return Result<FiscalDocumentDto>.Failure(AppErrors.Conflict("fiscal.correction_refused", $"A SEFAZ não aceitou a carta de correção: {answer.Message}"));
            }

            document.Correct(sequence, text, answer.Protocol ?? string.Empty, answer.Xml, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return FiscalService.ToDto(document);
        });

    public Task<Result<NumberGapDto>> GiveUpNumbersAsync(Guid establishmentId, NumberGapRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<NumberGapDto>(async () =>
        {
            var profile = await RequireProfileAsync(establishmentId, cancellationToken);

            // A number already used by a note cannot be given up.
            var used = await documents.AnyAsync(
                item => item.EstablishmentId == establishmentId && item.Model == request.Model && item.Series == request.Series
                    && item.Number >= request.FromNumber && item.Number <= request.ToNumber
                    && item.Status != FiscalDocumentStatus.Rejected,
                cancellationToken);

            if (used)
            {
                return Result<NumberGapDto>.Failure(AppErrors.Conflict("fiscal.number_used", "Há nota emitida ou pendente nessa faixa. Inutilize só números que nunca foram usados."));
            }

            var gap = new FiscalNumberGap(profile.TenantId, establishmentId, request.Model, request.Series, request.FromNumber, request.ToNumber, profile.Environment, request.Reason, clock.GetUtcNow());
            gaps.Add(gap);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var answer = await emitter.GiveUpNumbersAsync(
                new DfeNumberGap(gap.Model, gap.Environment, profile.State, profile.Cnpj, gap.Series, gap.FromNumber, gap.ToNumber, clock.GetUtcNow().Year, gap.Reason, Credentials(profile)),
                CancellationToken.None);

            gap.Finish(answer.Accepted, answer.Protocol, answer.Message, answer.Xml);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return NumberGapDto.From(gap);
        });

    /// <summary>Builds the neutral note: lines with freight and discount spread by value, and the payment of the order.</summary>
    public static DfeIssue BuildIssue(
        FiscalProfile profile,
        FiscalDocument document,
        Order order,
        IReadOnlyDictionary<Guid, ProductFiscalData> taxes,
        NfeRecipientRequest? recipient,
        DateTimeOffset now,
        DfeCredentials? credentials = null)
    {
        var freight = Spread(order.DeliveryFeeInCents, order.Items.Select(item => item.TotalInCents).ToList());
        var discount = Spread(order.DiscountInCents, order.Items.Select(item => item.TotalInCents).ToList());

        var lines = order.Items.Select((item, index) =>
        {
            var tax = taxes[item.MenuItemId];
            return new DfeLine(
                item.MenuItemId.ToString("N")[..12].ToUpperInvariant(),
                item.Options.Count == 0 ? item.Name : $"{item.Name} ({string.Join(", ", item.Options.Select(option => option.Name))})",
                item.Quantity,
                item.TotalInCents,
                freight[index],
                discount[index],
                tax.Ncm,
                tax.Cfop,
                tax.Cest,
                tax.IcmsCst,
                tax.IcmsRate,
                tax.PisCofinsCst,
                tax.PisRate,
                tax.CofinsRate,
                tax.HasPisCofinsValues);
        }).ToList();

        var kind = order.PaymentMethod switch
        {
            PaymentMethod.Cash => DfePaymentKind.Cash,
            PaymentMethod.Pix => DfePaymentKind.Pix,
            PaymentMethod.CardOnDelivery => DfePaymentKind.CreditCard,
            _ => DfePaymentKind.Other,
        };

        return new DfeIssue(
            document.Model,
            document.Environment,
            document.Series,
            document.Number,
            document.NumericCode,
            now,
            new DfeIssuer(profile.Cnpj, profile.LegalName, profile.TradeName, profile.StateRegistration, profile.Street, profile.Number, profile.District, profile.CityIbgeCode, profile.City, profile.State, profile.PostalCode),
            recipient is null
                ? null
                : new DfeRecipient(recipient.Document, recipient.Name, recipient.StateRegistration, recipient.Street, recipient.Number, recipient.District, recipient.CityIbgeCode, recipient.City, recipient.State, recipient.PostalCode, recipient.Email),
            lines,
            [new DfePayment(kind, order.TotalInCents)],
            order.FulfillmentType == FulfillmentType.Delivery,
            $"Pedido {order.Code}",
            credentials ?? new DfeCredentials([], string.Empty, null, null));
    }

    private DfeIssue BuildIssue(FiscalProfile profile, FiscalDocument document, Order order, IReadOnlyDictionary<Guid, ProductFiscalData> taxes, NfeRecipientRequest? recipient, DateTimeOffset now) =>
        BuildIssue(profile, document, order, taxes, recipient, now, Credentials(profile));

    /// <summary>Spreads a value over the lines by their weight; the rounding rest goes to the last line.</summary>
    public static long[] Spread(long amount, IReadOnlyList<long> weights)
    {
        var result = new long[weights.Count];
        var total = weights.Sum();

        if (amount <= 0 || total <= 0)
        {
            return result;
        }

        long given = 0;

        for (var index = 0; index < weights.Count - 1; index++)
        {
            result[index] = amount * weights[index] / total;
            given += result[index];
        }

        result[^1] = amount - given;
        return result;
    }

    private static void Apply(FiscalDocument document, DfeResult result)
    {
        var now = DateTimeOffset.UtcNow;

        switch (result.Outcome)
        {
            case DfeOutcome.Authorized:
                document.Authorize(result.AccessKey, result.Protocol, result.Xml, result.Message, now);
                break;
            case DfeOutcome.Rejected:
                document.Reject(result.Message, now);
                break;
            default:
                document.MarkUnanswered(result.Message, now);
                break;
        }
    }

    private DfeCredentials Credentials(FiscalProfile profile)
    {
        if (!profile.HasCertificate)
        {
            throw new InvalidOperationException("Envie o certificado A1 da loja antes de falar com a SEFAZ.");
        }

        return new DfeCredentials(
            Convert.FromBase64String(protector.Unprotect(profile.EncryptedCertificate!)),
            protector.Unprotect(profile.EncryptedCertificatePassword!),
            profile.CscId,
            profile.EncryptedCscToken is null ? null : protector.Unprotect(profile.EncryptedCscToken));
    }

    private async Task<FiscalProfile> RequireProfileAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        await profiles.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken)
            ?? throw new InvalidOperationException("Preencha os dados do emitente antes.");

    private static bool IsValidDocument(string document)
    {
        var digits = new string((document ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 14 ? DocumentValidator.IsValidCnpj(digits) : digits.Length == 11 && DocumentValidator.IsValidCpf(digits);
    }
}
