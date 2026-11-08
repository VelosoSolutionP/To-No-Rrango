using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Payments;

public enum PaymentChannel
{
    Pix = 1,
    Card = 2
}

public enum ChargeStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Refunded = 5
}

public sealed class Payment : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid OrderId { get; private set; }
    public string Provider { get; private set; } = "MercadoPago";
    public string ProviderPaymentId { get; private set; } = string.Empty;
    public PaymentChannel Channel { get; private set; }
    public ChargeStatus Status { get; private set; }
    public string? StatusDetail { get; private set; }
    public long AmountInCents { get; private set; }
    public string? PixQrCode { get; private set; }
    public string? PixQrCodeBase64 { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset LastCheckedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Payment() { }

    public Payment(
        Guid tenantId,
        Guid establishmentId,
        Guid orderId,
        string providerPaymentId,
        PaymentChannel channel,
        ChargeStatus status,
        string? statusDetail,
        long amountInCents,
        string? pixQrCode,
        string? pixQrCodeBase64,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        OrderId = Ensure.NotEmpty(orderId, nameof(OrderId));
        ProviderPaymentId = Ensure.NotNullOrWhiteSpace(providerPaymentId, nameof(ProviderPaymentId));
        Channel = channel;
        Status = status;
        StatusDetail = statusDetail;
        AmountInCents = Ensure.NotNegative(amountInCents, nameof(AmountInCents));
        PixQrCode = pixQrCode;
        PixQrCodeBase64 = pixQrCodeBase64;
        ExpiresAt = expiresAt;
        LastCheckedAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public bool IsReusable(DateTimeOffset now) =>
        Status == ChargeStatus.Pending && (ExpiresAt is null || ExpiresAt > now);

    public void MarkChecked(DateTimeOffset now) => LastCheckedAt = now;

    public void UpdateStatus(ChargeStatus status, string? statusDetail, DateTimeOffset now)
    {
        Status = status;
        StatusDetail = statusDetail;
        LastCheckedAt = now;
        UpdatedAt = now;
    }
}

public sealed class PaymentSettings : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string? EncryptedAccessToken { get; private set; }
    public string? EncryptedWebhookSecret { get; private set; }
    public string? PublicKey { get; private set; }
    public bool IsPixEnabled { get; private set; }
    public bool IsCardEnabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PaymentSettings() { }

    public PaymentSettings(Guid tenantId, Guid establishmentId, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        UpdatedAt = now;
    }

    public bool CanChargePix => IsPixEnabled && EncryptedAccessToken is not null;

    public bool CanChargeCard => IsCardEnabled && EncryptedAccessToken is not null && PublicKey is not null;

    public void Update(
        string? encryptedAccessToken,
        string? encryptedWebhookSecret,
        string? publicKey,
        bool isPixEnabled,
        bool isCardEnabled,
        DateTimeOffset now)
    {
        EncryptedAccessToken = encryptedAccessToken ?? EncryptedAccessToken;
        EncryptedWebhookSecret = encryptedWebhookSecret ?? EncryptedWebhookSecret;
        PublicKey = string.IsNullOrWhiteSpace(publicKey) ? PublicKey : publicKey.Trim();
        IsPixEnabled = isPixEnabled;
        IsCardEnabled = isCardEnabled;
        UpdatedAt = now;
    }
}
