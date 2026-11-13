using System.Security.Cryptography;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Fiscal;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

// ---------- Contract ----------

public sealed record AccountantSignUpRequest(string FirmName, string Document, string? Crc, string? Phone, string Name, string Email, string Password, string InviteCode);

public sealed record AccountantLoginRequest(string Email, string Password);

public sealed record AccountantFirmDto(Guid Id, string Name, string Document, string Crc, string Email, string? Phone, bool IsPartner, DateTimeOffset? PartnerSince, DateTimeOffset? PartnerRequestedAt, PartnerKind Kind = PartnerKind.Accounting);

public sealed record AccountantSessionDto(string Token, DateTimeOffset ExpiresAt, string Name, string Email, AccountantFirmDto Firm);

public sealed record AccountantMeDto(string Name, string Email, AccountantFirmDto Firm);

public sealed record AcceptInviteRequest(string Code);

/// <summary>One company in the office's list, with what needs attention.</summary>
public sealed record AccountantCompanyDto(Guid LinkId, Guid EstablishmentId, string Name, DateTimeOffset? Since, int OpenRequests, int Unread, PartnerKind Kind = PartnerKind.Accounting);

public sealed record InviteAccountantRequest(string Email, PartnerKind Kind = PartnerKind.Accounting);

public sealed record AccountantInviteDto(AccountantLinkDto Link, string Code);

public sealed record AccountantLinkDto(
    Guid Id,
    PartnerKind Kind,
    string InvitedEmail,
    AccountantLinkStatus Status,
    string? FirmName,
    string? FirmCrc,
    bool FirmIsPartner,
    string InvitedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CodeExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? InviteEmailedAt = null,
    string? InviteEmailError = null);

public sealed record PostAccountantMessageRequest(string Text, bool IsRequest = false);

public sealed record AccountantMessageDto(
    Guid Id,
    bool FromAccountant,
    bool Mine,
    string Author,
    string Text,
    bool IsRequest,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt);

public sealed record AccountantDeskDto(IReadOnlyList<AccountantLinkDto> Links, IReadOnlyList<AccountantMessageDto> Messages);

public sealed record AccountantFirmSummaryDto(AccountantFirmDto Firm, int Users, int ActiveCompanies, DateTimeOffset CreatedAt);

public sealed record SetPartnerRequest(bool IsPartner);

/// <summary>Who is talking on a conversation: the store (a panel user) or the office (a portal user).</summary>
public sealed record AccountantSide(bool IsAccountant, string Author);

// ---------- Office: sign up, sign in, companies ----------

public sealed class AccountantPortalService(
    IRepository<AccountantFirm> firms,
    IRepository<AccountantUser> users,
    IRepository<AccountantLink> links,
    IRepository<AccountantMessage> messages,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public const int MinimumPasswordLength = 8;
    private static string? _noSuchUserHash;

    /// <summary>An office is born from an invitation: no store, no account. This keeps the portal closed to strangers.</summary>
    public Task<Result<AccountantSessionDto>> SignUpAsync(AccountantSignUpRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantSessionDto>(async () =>
        {
            var document = new string((request.Document ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

            if (string.IsNullOrWhiteSpace(request.FirmName) || string.IsNullOrWhiteSpace(request.Name))
            {
                return Result<AccountantSessionDto>.Failure(AppErrors.Validation("Informe o nome do escritório e o seu nome."));
            }

            if (!(document.Length == 14 ? DocumentValidator.IsValidCnpj(document) : document.Length == 11 && DocumentValidator.IsValidCpf(document)))
            {
                return Result<AccountantSessionDto>.Failure(AppErrors.Validation("CNPJ do escritório ou CPF do contador inválido."));
            }

            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            {
                return Result<AccountantSessionDto>.Failure(AppErrors.Validation("E-mail inválido."));
            }

            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinimumPasswordLength)
            {
                return Result<AccountantSessionDto>.Failure(AppErrors.Validation($"A senha precisa ter pelo menos {MinimumPasswordLength} caracteres."));
            }

            var email = AccountantUser.NormalizeEmail(request.Email);

            if (await users.AnyAsync(user => user.Email == email, cancellationToken))
            {
                return Result<AccountantSessionDto>.Failure(AppErrors.Conflict("accountant.exists", "Este e-mail já tem conta. Entre e use o código em \"Adicionar empresa\"."));
            }

            var now = clock.GetUtcNow();
            var link = await FindInviteAsync(request.InviteCode, cancellationToken);

            if (link is null || !link.CanBeAcceptedBy(email, now))
            {
                return Result<AccountantSessionDto>.Failure(AppErrors.Validation("Código de convite inválido, vencido ou feito para outro e-mail."));
            }

            // The invitation says what the partner is: an accounting office needs its CRC.
            if (link.Kind == PartnerKind.Accounting && (string.IsNullOrWhiteSpace(request.Crc) || request.Crc.Trim().Length is < 4 or > 20))
            {
                return Result<AccountantSessionDto>.Failure(AppErrors.Validation("Informe o CRC (ex.: MG-123456/O)."));
            }

            var firm = new AccountantFirm(request.FirmName, document, request.Crc, email, request.Phone, now, link.Kind);
            var user = new AccountantUser(firm.Id, request.Name, email, hasher.Hash(request.Password), now);
            user.RegisterSuccessfulLogin(now);
            firms.Add(firm);
            users.Add(user);
            link.Accept(firm.Id, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Session(user, firm);
        });

    public async Task<Result<AccountantSessionDto>> LoginAsync(AccountantLoginRequest request, CancellationToken cancellationToken)
    {
        var invalid = Result<AccountantSessionDto>.Failure(Error.Unauthorized("accountant.invalid_credentials", "E-mail ou senha incorretos."));

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return invalid;
        }

        var email = AccountantUser.NormalizeEmail(request.Email);
        var user = await users.FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
        var now = clock.GetUtcNow();

        if (user is null)
        {
            // Same work as a real check, so the answer time does not tell which e-mails exist.
            hasher.Verify(request.Password, _noSuchUserHash ??= hasher.Hash(Guid.NewGuid().ToString()));
            return invalid;
        }

        if (user.IsLocked(now))
        {
            return Result<AccountantSessionDto>.Failure(Error.TooManyRequests("accountant.locked", "Muitas tentativas incorretas. Aguarde alguns minutos."));
        }

        if (!hasher.Verify(request.Password, user.PasswordHash) || !user.IsActive)
        {
            user.RegisterFailedLogin(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return invalid;
        }

        var firm = await firms.FirstOrDefaultAsync(item => item.Id == user.FirmId, cancellationToken);

        if (firm is null)
        {
            return invalid;
        }

        user.RegisterSuccessfulLogin(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Session(user, firm);
    }

    public async Task<Result<AccountantMeDto>> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FirstOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        var firm = user is null ? null : await firms.FirstOrDefaultAsync(item => item.Id == user.FirmId, cancellationToken);

        return user is null || firm is null
            ? Result<AccountantMeDto>.Failure(Error.Unauthorized("accountant.gone", "Conta não encontrada."))
            : new AccountantMeDto(user.Name, user.Email, ToDto(firm));
    }

    public Task<bool> IsActiveAsync(Guid userId, Guid firmId, CancellationToken cancellationToken) =>
        users.AnyAsync(user => user.Id == userId && user.FirmId == firmId && user.IsActive, cancellationToken);

    public async Task<IReadOnlyList<AccountantCompanyDto>> ListCompaniesAsync(Guid firmId, CancellationToken cancellationToken)
    {
        var active = await links.ListAsync(link => link.FirmId == firmId && link.Status == AccountantLinkStatus.Active, link => link.EstablishmentName, cancellationToken, take: 1000);
        var ids = active.Select(link => link.Id).ToList();
        var waiting = await messages.ListAsync(
            message => ids.Contains(message.LinkId) && ((message.IsRequest && message.ResolvedAt == null) || (!message.FromAccountant && message.ReadAt == null)),
            message => message.CreatedAt,
            cancellationToken,
            take: 5000);

        return active.Select(link => new AccountantCompanyDto(
                link.Id,
                link.EstablishmentId,
                link.EstablishmentName,
                link.AcceptedAt,
                waiting.Count(message => message.LinkId == link.Id && message.IsRequest && message.ResolvedAt == null),
                waiting.Count(message => message.LinkId == link.Id && !message.FromAccountant && message.ReadAt == null),
                link.Kind))
            .ToList();
    }

    /// <summary>An office that already has an account adds one more company with the store's code.</summary>
    public Task<Result<AccountantCompanyDto>> AcceptAsync(Guid firmId, string email, AcceptInviteRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantCompanyDto>(async () =>
        {
            var link = await FindInviteAsync(request.Code, cancellationToken);
            var now = clock.GetUtcNow();

            if (link is null || !link.CanBeAcceptedBy(email, now))
            {
                return Result<AccountantCompanyDto>.Failure(AppErrors.Validation("Código de convite inválido, vencido ou feito para outro e-mail."));
            }

            var firm = await firms.FirstOrDefaultAsync(item => item.Id == firmId, cancellationToken);

            if (firm is null || firm.Kind != link.Kind)
            {
                return Result<AccountantCompanyDto>.Failure(AppErrors.Validation(link.Kind == PartnerKind.SoftwareHouse
                    ? "Este convite é para uma software house, e sua conta é de escritório contábil."
                    : "Este convite é para escritório contábil, e sua conta é de software house."));
            }

            if (await links.AnyAsync(item => item.FirmId == firmId && item.EstablishmentId == link.EstablishmentId && item.Status == AccountantLinkStatus.Active, cancellationToken))
            {
                link.Revoke(now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<AccountantCompanyDto>.Failure(AppErrors.Conflict("accountant.already_linked", "Seu escritório já atende esta empresa."));
            }

            link.Accept(firmId, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new AccountantCompanyDto(link.Id, link.EstablishmentId, link.EstablishmentName, link.AcceptedAt, 0, 0, link.Kind);
        });

    /// <summary>The active link of this office to this store, or null: every company route goes through here.</summary>
    public Task<AccountantLink?> FindLinkAsync(Guid firmId, Guid establishmentId, CancellationToken cancellationToken) =>
        links.FirstOrDefaultAsync(link => link.FirmId == firmId && link.EstablishmentId == establishmentId && link.Status == AccountantLinkStatus.Active, cancellationToken);

    public Task<Result<AccountantFirmDto>> RequestPartnershipAsync(Guid firmId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantFirmDto>(async () =>
        {
            var firm = await firms.FirstOrDefaultAsync(item => item.Id == firmId, cancellationToken);

            if (firm is null)
            {
                return Result<AccountantFirmDto>.Failure(AppErrors.NotFound("accountant.firm_not_found", "Escritório não encontrado."));
            }

            firm.RequestPartnership(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(firm);
        });

    // ----- Platform (Veloso Solution) side of the partner program -----

    public async Task<IReadOnlyList<AccountantFirmSummaryDto>> ListFirmsAsync(CancellationToken cancellationToken)
    {
        var all = await firms.ListAsync(_ => true, firm => firm.CreatedAt, cancellationToken, descending: true, take: 2000);
        var activeLinks = await links.ListAsync(link => link.Status == AccountantLinkStatus.Active, link => link.CreatedAt, cancellationToken, take: 100_000);
        var people = await users.ListAsync(user => user.IsActive, user => user.CreatedAt, cancellationToken, take: 100_000);

        return all.Select(firm => new AccountantFirmSummaryDto(
                ToDto(firm),
                people.Count(user => user.FirmId == firm.Id),
                activeLinks.Count(link => link.FirmId == firm.Id),
                firm.CreatedAt))
            .ToList();
    }

    public Task<Result<AccountantFirmDto>> SetPartnerAsync(Guid firmId, SetPartnerRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantFirmDto>(async () =>
        {
            var firm = await firms.FirstOrDefaultAsync(item => item.Id == firmId, cancellationToken);

            if (firm is null)
            {
                return Result<AccountantFirmDto>.Failure(AppErrors.NotFound("accountant.firm_not_found", "Escritório não encontrado."));
            }

            firm.SetPartner(request.IsPartner, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(firm);
        });

    private async Task<AccountantLink?> FindInviteAsync(string? code, CancellationToken cancellationToken)
    {
        var clean = AccountantDeskService.CleanCode(code);

        if (clean.Length != AccountantDeskService.CodeLength)
        {
            return null;
        }

        var hash = FiscalApiRules.HashKey(clean);
        return await links.FirstOrDefaultAsync(link => link.CodeHash == hash, cancellationToken);
    }

    private AccountantSessionDto Session(AccountantUser user, AccountantFirm firm)
    {
        var (token, expiresAt) = tokens.IssueAccountant(user.Id, firm.Id, user.Name, user.Email, firm.Kind.ToString());
        return new AccountantSessionDto(token, expiresAt, user.Name, user.Email, ToDto(firm));
    }

    public static AccountantFirmDto ToDto(AccountantFirm firm) =>
        new(firm.Id, firm.Name, firm.Document, firm.Crc, firm.Email, firm.Phone, firm.IsPartner, firm.PartnerSince, firm.PartnerRequestedAt, firm.Kind);
}

// ---------- Store and office together: invitations and the conversation ----------

public sealed class AccountantDeskService(
    IRepository<AccountantLink> links,
    IRepository<AccountantMessage> messages,
    IRepository<AccountantFirm> firms,
    IRepository<Establishment> establishments,
    IRepository<FiscalApiKey> apiKeys,
    IRepository<FiscalWebhook> webhooks,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IEmailSender? mail = null,
    IAppUrls? urls = null)
{
    public const int CodeLength = 10;

    // No 0/O, 1/I/L: the code is read over the phone.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string CleanCode(string? code) =>
        new((code ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public Task<Result<AccountantInviteDto>> InviteAsync(Guid tenantId, Guid establishmentId, InviteAccountantRequest request, string actor, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantInviteDto>(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@') || request.Email.Length > 200)
            {
                return Result<AccountantInviteDto>.Failure(AppErrors.Validation("Informe o e-mail do contador."));
            }

            var establishment = await establishments.FirstOrDefaultAsync(item => item.Id == establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<AccountantInviteDto>.Failure(AppErrors.NotFound("establishment.not_found", "Loja não encontrada."));
            }

            var now = clock.GetUtcNow();
            var email = AccountantUser.NormalizeEmail(request.Email);

            // A new invitation to the same e-mail replaces the pending one: only the newest code works.
            if (!Enum.IsDefined(request.Kind))
            {
                return Result<AccountantInviteDto>.Failure(AppErrors.Validation("Escolha contador ou software house."));
            }

            foreach (var pending in await links.ListAsync(link => link.EstablishmentId == establishmentId && link.InvitedEmail == email && link.Status == AccountantLinkStatus.Invited, link => link.CreatedAt, cancellationToken))
            {
                (await links.FirstOrDefaultAsync(link => link.Id == pending.Id, cancellationToken))?.Revoke(now);
            }

            var code = string.Create(CodeLength, 0, (span, _) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    span[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
                }
            });
            var link = new AccountantLink(tenantId, establishmentId, establishment.Name, email, FiscalApiRules.HashKey(code), actor, now, request.Kind);
            links.Add(link);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var shown = $"{code[..5]}-{code[5..]}";

            // Owner (13/11): "quando eu gerar o convite já enviar pra ela automaticamente". The code is
            // already saved; a mail failure only gets recorded, and the code can still be passed by hand.
            link.RecordInviteEmail(clock.GetUtcNow(), await SendInviteAsync(link, establishment.Name, shown, cancellationToken));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new AccountantInviteDto(ToDto(link, null), shown);
        });

    public Task<Result<AccountantLinkDto>> RevokeAsync(Guid establishmentId, Guid linkId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantLinkDto>(async () =>
        {
            var link = await links.FirstOrDefaultAsync(item => item.Id == linkId && item.EstablishmentId == establishmentId, cancellationToken);

            if (link is null)
            {
                return Result<AccountantLinkDto>.Failure(AppErrors.NotFound("accountant.link_not_found", "Acesso não encontrado."));
            }

            var now = clock.GetUtcNow();
            link.Revoke(now);

            // A software house leaves with everything it plugged in: its keys stop and its webhooks go quiet.
            if (link.FirmId is { } partner)
            {
                foreach (var listed in await apiKeys.ListAsync(key => key.EstablishmentId == establishmentId && key.PartnerFirmId == partner && key.RevokedAt == null, key => key.CreatedAt, cancellationToken))
                {
                    (await apiKeys.FirstOrDefaultAsync(key => key.Id == listed.Id, cancellationToken))?.Revoke(now);
                }

                foreach (var listed in await webhooks.ListAsync(hook => hook.EstablishmentId == establishmentId && hook.PartnerFirmId == partner && hook.IsActive, hook => hook.CreatedAt, cancellationToken))
                {
                    (await webhooks.FirstOrDefaultAsync(hook => hook.Id == listed.Id, cancellationToken))?.Deactivate();
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(link, link.FirmId is { } id ? await firms.FirstOrDefaultAsync(firm => firm.Id == id, cancellationToken) : null);
        });

    /// <summary>The store's view: who has access and the whole conversation.</summary>
    public async Task<AccountantDeskDto> GetDeskAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var all = await links.ListAsync(link => link.EstablishmentId == establishmentId, link => link.CreatedAt, cancellationToken, descending: true, take: 50);
        var firmIds = all.Where(link => link.FirmId != null).Select(link => link.FirmId!.Value).Distinct().ToList();
        var offices = (await firms.ListAsync(firm => firmIds.Contains(firm.Id), firm => firm.Name, cancellationToken)).ToDictionary(firm => firm.Id);

        return new AccountantDeskDto(
            all.Where(link => link.Status != AccountantLinkStatus.Revoked || link.AcceptedAt != null)
                .Select(link => ToDto(link, link.FirmId is { } id ? offices.GetValueOrDefault(id) : null))
                .ToList(),
            await ConversationAsync(establishmentId, null, new AccountantSide(false, string.Empty), cancellationToken));
    }

    /// <summary>Messages of the store, newest last. Reading marks as read what the other side wrote.</summary>
    public async Task<IReadOnlyList<AccountantMessageDto>> ConversationAsync(Guid establishmentId, Guid? linkId, AccountantSide side, CancellationToken cancellationToken)
    {
        var list = await messages.ListAsync(
            message => message.EstablishmentId == establishmentId && (linkId == null || message.LinkId == linkId),
            message => message.CreatedAt,
            cancellationToken,
            descending: true,
            take: 300);
        var unread = list.Where(message => message.FromAccountant != side.IsAccountant && message.ReadAt == null).Select(message => message.Id).ToList();

        if (unread.Count > 0)
        {
            var now = clock.GetUtcNow();

            foreach (var id in unread)
            {
                (await messages.FirstOrDefaultAsync(message => message.Id == id, cancellationToken))?.MarkRead(now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return list.Reverse().Select(message => ToDto(message, side)).ToList();
    }

    public Task<Result<AccountantMessageDto>> PostAsync(Guid establishmentId, Guid? linkId, AccountantSide side, PostAccountantMessageRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantMessageDto>(async () =>
        {
            var text = (request.Text ?? string.Empty).Trim();

            if (text.Length is < 1 or > 2000)
            {
                return Result<AccountantMessageDto>.Failure(AppErrors.Validation("Escreva a mensagem (até 2000 caracteres)."));
            }

            // The store writes to its current accountant; the office writes on its own link.
            var link = linkId is { } id
                ? await links.FirstOrDefaultAsync(item => item.Id == id && item.EstablishmentId == establishmentId && item.Status == AccountantLinkStatus.Active, cancellationToken)
                : (await links.ListAsync(item => item.EstablishmentId == establishmentId && item.Status == AccountantLinkStatus.Active, item => item.AcceptedAt, cancellationToken, descending: true, take: 1)).FirstOrDefault();

            if (link is null)
            {
                return Result<AccountantMessageDto>.Failure(AppErrors.Conflict("accountant.no_link", "Nenhum contador com acesso ativo a esta loja. Convide o contador primeiro."));
            }

            var message = new AccountantMessage(link, side.IsAccountant, side.Author, text, request.IsRequest, clock.GetUtcNow());
            messages.Add(message);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(message, side);
        });

    public Task<Result<AccountantMessageDto>> ResolveAsync(Guid establishmentId, Guid? linkId, Guid messageId, AccountantSide side, CancellationToken cancellationToken) =>
        Guarded.RunAsync<AccountantMessageDto>(async () =>
        {
            var message = await messages.FirstOrDefaultAsync(
                item => item.Id == messageId && item.EstablishmentId == establishmentId && (linkId == null || item.LinkId == linkId),
                cancellationToken);

            if (message is not { IsRequest: true })
            {
                return Result<AccountantMessageDto>.Failure(AppErrors.NotFound("accountant.request_not_found", "Pedido não encontrado."));
            }

            message.Resolve(side.Author, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(message, side);
        });

    private static AccountantLinkDto ToDto(AccountantLink link, AccountantFirm? firm) =>
        new(link.Id, link.Kind, link.InvitedEmail, link.Status, firm?.Name, firm?.Crc, firm?.IsPartner ?? false, link.InvitedBy, link.CreatedAt, link.CodeExpiresAt, link.AcceptedAt, link.RevokedAt, link.InviteEmailedAt, link.InviteEmailError);

    /// <summary>Null when the mail server accepted it; otherwise the reason, shown on the invitation.</summary>
    private async Task<string?> SendInviteAsync(AccountantLink link, string storeName, string code, CancellationToken cancellationToken)
    {
        if (mail is null || !mail.IsConfigured)
        {
            return "Envio de e-mail não configurado no servidor. Passe o código ao parceiro.";
        }

        var portal = urls?.AccountantPortalUrl() ?? "/contador";
        var address = $"{portal}?convite={Uri.EscapeDataString(code)}&email={Uri.EscapeDataString(link.InvitedEmail)}";
        var role = link.Kind == PartnerKind.Accounting ? "contador(a)" : "software house";
        var expires = link.CodeExpiresAt?.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy 'às' HH:mm") ?? "7 dias";
        var text =
            $"Olá!\n\n{storeName} convidou você como {role} no To No Rango.\n\n" +
            $"Código do convite: {code}\n" +
            $"Abra o portal de parceiros: {address}\n\n" +
            $"O código vale até {expires} e só funciona com este e-mail ({link.InvitedEmail}).\n" +
            "Se você já tem acesso ao portal, entre e use o código em \"Adicionar empresa\".\n\n" +
            "Se não esperava este convite, ignore esta mensagem.\n\nTo No Rango · Veloso Solution";
        var safeStore = System.Net.WebUtility.HtmlEncode(storeName);
        var html =
            "<div style=\"font-family:Arial,sans-serif;max-width:520px;color:#1a1a1a\">" +
            $"<p>Olá!</p><p><b>{safeStore}</b> convidou você como <b>{role}</b> no To No Rango.</p>" +
            $"<p style=\"font-size:13px;color:#555\">Código do convite</p><p style=\"font-size:26px;font-weight:bold;letter-spacing:3px;margin:0 0 18px\">{code}</p>" +
            $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(address)}\" style=\"background:#ff6a1f;color:#fff;padding:12px 22px;text-decoration:none;font-weight:bold;display:inline-block\">Aceitar convite</a></p>" +
            $"<p style=\"font-size:13px;color:#555\">O código vale até {expires} e só funciona com este e-mail. Se você já tem acesso ao portal, entre e use o código em \"Adicionar empresa\".</p>" +
            "<p style=\"font-size:12px;color:#888\">Se não esperava este convite, ignore esta mensagem.<br>To No Rango · Veloso Solution</p></div>";

        try
        {
            return await mail.SendAsync(new EmailMessage(link.InvitedEmail, $"{storeName} convidou você para o To No Rango", text, html), cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return $"Falha no envio: {failure.Message}";
        }
    }

    private static AccountantMessageDto ToDto(AccountantMessage message, AccountantSide side) =>
        new(message.Id, message.FromAccountant, message.FromAccountant == side.IsAccountant, message.Author, message.Text, message.IsRequest, message.ResolvedAt, message.ResolvedBy, message.ReadAt, message.CreatedAt);
}
