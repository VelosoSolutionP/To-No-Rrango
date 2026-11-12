using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Fiscal;

/// <summary>
/// An accounting office. It is not a tenant: one office serves many stores of many tenants, and sees
/// only the stores that invited it. The Veloso Solution approves the partner offices.
/// </summary>
public sealed class AccountantFirm : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>CNPJ of the office or CPF of the accountant, digits only.</summary>
    public string Document { get; private set; } = string.Empty;
    public string Crc { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public bool IsPartner { get; private set; }
    public DateTimeOffset? PartnerSince { get; private set; }
    public DateTimeOffset? PartnerRequestedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AccountantFirm() { }

    public AccountantFirm(string name, string document, string crc, string email, string? phone, DateTimeOffset now)
    {
        Id = GuidV7.New();
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(name)).Trim(), 120, nameof(name));
        Document = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(document, nameof(document)), 14, nameof(document));
        Crc = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(crc, nameof(crc)).Trim().ToUpperInvariant(), 20, nameof(crc));
        Email = AccountantUser.NormalizeEmail(email);
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim()[..Math.Min(phone.Trim().Length, 20)];
        CreatedAt = now;
    }

    public void RequestPartnership(DateTimeOffset now) => PartnerRequestedAt ??= now;

    public void SetPartner(bool isPartner, DateTimeOffset now)
    {
        IsPartner = isPartner;
        PartnerSince = isPartner ? PartnerSince ?? now : null;
    }
}

/// <summary>A person of the office who signs in to the accountant portal.</summary>
public sealed class AccountantUser : AggregateRoot
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public Guid FirmId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AccountantUser() { }

    public AccountantUser(Guid firmId, string name, string email, string passwordHash, DateTimeOffset now)
    {
        Id = GuidV7.New();
        FirmId = Ensure.NotEmpty(firmId, nameof(firmId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(name)).Trim(), 120, nameof(name));
        Email = NormalizeEmail(email);
        PasswordHash = Ensure.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        CreatedAt = now;
    }

    public static string NormalizeEmail(string email) =>
        Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(email, nameof(email)).Trim(), 200, nameof(email)).ToLowerInvariant();

    public bool IsLocked(DateTimeOffset now) => LockedUntil is not null && LockedUntil > now;

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedAttempts)
        {
            LockedUntil = now.Add(LockoutDuration);
            FailedLoginAttempts = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }
}

public enum AccountantLinkStatus
{
    Invited = 1,
    Active = 2,
    Revoked = 3
}

/// <summary>
/// A store's permission for an office to read its fiscal data. It starts as an invitation to one
/// e-mail with a one-time code; only an office user with that e-mail can accept it.
/// </summary>
public sealed class AccountantLink : AggregateRoot
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }

    /// <summary>Kept here so the office lists its companies without opening every tenant.</summary>
    public string EstablishmentName { get; private set; } = string.Empty;
    public string InvitedEmail { get; private set; } = string.Empty;
    public string? CodeHash { get; private set; }
    public DateTimeOffset? CodeExpiresAt { get; private set; }
    public Guid? FirmId { get; private set; }
    public AccountantLinkStatus Status { get; private set; }
    public string InvitedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private AccountantLink() { }

    public AccountantLink(Guid tenantId, Guid establishmentId, string establishmentName, string email, string codeHash, string invitedBy, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(tenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(establishmentId));
        EstablishmentName = Ensure.NotNullOrWhiteSpace(establishmentName, nameof(establishmentName)).Trim()[..Math.Min(establishmentName.Trim().Length, 120)];
        InvitedEmail = AccountantUser.NormalizeEmail(email);
        CodeHash = Ensure.NotNullOrWhiteSpace(codeHash, nameof(codeHash));
        CodeExpiresAt = now + InviteLifetime;
        InvitedBy = string.IsNullOrWhiteSpace(invitedBy) ? "desconhecido" : invitedBy.Trim()[..Math.Min(invitedBy.Trim().Length, 160)];
        Status = AccountantLinkStatus.Invited;
        CreatedAt = now;
    }

    public bool CanBeAcceptedBy(string email, DateTimeOffset now) =>
        Status == AccountantLinkStatus.Invited && CodeExpiresAt > now && InvitedEmail == AccountantUser.NormalizeEmail(email);

    public void Accept(Guid firmId, DateTimeOffset now)
    {
        if (Status != AccountantLinkStatus.Invited || CodeExpiresAt <= now)
        {
            throw new InvalidOperationException("Convite vencido ou já usado. Peça um novo à loja.");
        }

        FirmId = Ensure.NotEmpty(firmId, nameof(firmId));
        Status = AccountantLinkStatus.Active;
        AcceptedAt = now;
        // Single use: the code can never open a second door.
        CodeHash = null;
        CodeExpiresAt = null;
    }

    public void Revoke(DateTimeOffset now)
    {
        Status = AccountantLinkStatus.Revoked;
        RevokedAt ??= now;
        CodeHash = null;
        CodeExpiresAt = null;
    }
}

/// <summary>Conversation between a store and its accountant. A request is a message that waits to be resolved.</summary>
public sealed class AccountantMessage : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid LinkId { get; private set; }
    public bool FromAccountant { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public bool IsRequest { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    /// <summary>When the other side first saw it.</summary>
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AccountantMessage() { }

    public AccountantMessage(AccountantLink link, bool fromAccountant, string author, string text, bool isRequest, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = link.TenantId;
        EstablishmentId = link.EstablishmentId;
        LinkId = link.Id;
        FromAccountant = fromAccountant;
        Author = string.IsNullOrWhiteSpace(author) ? "desconhecido" : author.Trim()[..Math.Min(author.Trim().Length, 160)];
        Text = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(text, nameof(text)).Trim(), 2000, nameof(text));
        IsRequest = isRequest;
        CreatedAt = now;
    }

    public void Resolve(string by, DateTimeOffset now)
    {
        if (!IsRequest)
        {
            throw new InvalidOperationException("Só pedido pode ser marcado como resolvido.");
        }

        ResolvedAt ??= now;
        ResolvedBy ??= by;
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
