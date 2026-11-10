using System.Linq.Expressions;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Application.Abstractions;

/// <summary>Tenant-filtered access to one entity. Lists are read-only; single reads are tracked.</summary>
public interface IRepository<T>
    where T : class
{
    Task<T?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);

    Task<IReadOnlyList<T>> ListAsync<TKey>(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, TKey>> orderBy,
        CancellationToken cancellationToken,
        bool descending = false,
        int take = 500);

    Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);

    void Add(T entity);

    void Remove(T entity);
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

public interface ITokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid tenantId, string name, string email, string role, string? velosoSession = null);
}

public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string cipherText);
}

public interface IModuleAccess
{
    Task<bool> IsAllowedAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken);
}

public interface IAppUrls
{
    string TrackingUrl(string orderCode);

    string MenuUrl(string slug);

    /// <summary>Public base address of this API, or null when it is not reachable from the internet.</summary>
    string? PublicApiUrl { get; }

    /// <summary>Address Evolution uses to reach this API; may differ from the public one on the same host.</summary>
    string? WebhookBaseUrl { get; }
}

public interface IOrderEvents
{
    Task PublishAsync(string orderCode);

    /// <summary>Yields "update" on each change of the order and "ping" as a keep-alive.</summary>
    IAsyncEnumerable<string> ListenAsync(string orderCode, TimeSpan heartbeat, CancellationToken cancellationToken);
}

public interface ICustomerStore
{
    /// <summary>Atomically creates the customer or adds this order to its history.</summary>
    Task RegisterOrderAsync(Order order, CancellationToken cancellationToken);
}

public interface IStockStore
{
    /// <summary>Applies a quantity change directly in the database so concurrent orders cannot lose updates.</summary>
    Task ApplyDeltaAsync(Guid stockItemId, decimal delta, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record GatewayPayment(
    string Id,
    string Status,
    string? StatusDetail,
    long AmountInCents,
    string? ExternalReference,
    string? PixQrCode,
    string? PixQrCodeBase64,
    DateTimeOffset? ExpiresAt);

public sealed record PixCharge(
    long AmountInCents,
    string Description,
    string ExternalReference,
    string PayerEmail,
    string IdempotencyKey,
    string? NotificationUrl);

public sealed record CardCharge(
    long AmountInCents,
    string Description,
    string ExternalReference,
    string Token,
    string PaymentMethodId,
    string? IssuerId,
    int Installments,
    string PayerEmail,
    string? DocumentType,
    string? DocumentNumber,
    string IdempotencyKey,
    string? NotificationUrl);

public sealed record CheckoutCharge(
    long AmountInCents,
    string Description,
    string ExternalReference,
    string ReturnUrl,
    DateTimeOffset ExpiresAt,
    string? NotificationUrl);

public sealed record GatewayCheckout(string Id, string Url);

public sealed class PaymentGatewayException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface IPaymentGateway
{
    Task<GatewayPayment> CreatePixAsync(string accessToken, PixCharge charge, CancellationToken cancellationToken);

    Task<GatewayPayment> CreateCardAsync(string accessToken, CardCharge charge, CancellationToken cancellationToken);

    Task<GatewayCheckout> CreateCheckoutAsync(string accessToken, CheckoutCharge charge, CancellationToken cancellationToken);

    Task<GatewayPayment> GetAsync(string accessToken, string paymentId, CancellationToken cancellationToken);

    Task RefundAsync(string accessToken, string paymentId, string idempotencyKey, CancellationToken cancellationToken);
}

public interface IWhatsAppSender
{
    bool IsConfigured { get; }

    /// <summary>True when the platform key allows creating instances and reading the QR Code.</summary>
    bool CanProvision { get; }

    string? PlatformApiKey { get; }

    Task SendTextAsync(string instanceName, string apiKey, string phone, string text, CancellationToken cancellationToken);

    Task SendImageAsync(string instanceName, string apiKey, string phone, string imageBase64, string? caption, CancellationToken cancellationToken);

    Task EnsureInstanceAsync(string instanceName, CancellationToken cancellationToken);

    Task SetWebhookAsync(string instanceName, string webhookUrl, CancellationToken cancellationToken);

    /// <summary>QR Code in base64 and, when a phone is given, the pairing code; both null when already connected.</summary>
    Task<(string? QrCode, string? PairingCode)> GetConnectCodesAsync(string instanceName, string? phone, CancellationToken cancellationToken);

    Task<(bool Connected, string? Number)> GetStateAsync(string instanceName, CancellationToken cancellationToken);

    /// <summary>Downloads the media of a received message, as base64 with its mime type.</summary>
    Task<(string Base64, string Mime)?> GetMediaAsync(string instanceName, string apiKey, string messageId, CancellationToken cancellationToken);

    /// <summary>Sends a voice note (ogg/opus in base64).</summary>
    Task SendAudioAsync(string instanceName, string apiKey, string phone, string audioBase64, CancellationToken cancellationToken);

    Task LogoutAsync(string instanceName, CancellationToken cancellationToken);
}

public sealed record CnpjData(
    string Cnpj,
    string LegalName,
    string? TradeName,
    string? Phone,
    string? Email,
    string? Address,
    string? Status);

public sealed record CepData(string PostalCode, string? Street, string? District, string City, string State);

public interface ILookupClient
{
    Task<CnpjData?> FindCnpjAsync(string cnpj, CancellationToken cancellationToken);

    Task<CepData?> FindCepAsync(string cep, CancellationToken cancellationToken);
}

public sealed record DashboardTopItem(string Name, int Quantity, long RevenueInCents);

public sealed record DashboardStatusCount(OrderStatus Status, int Count);

public sealed record DashboardPaymentTotal(PaymentMethod Method, int Count, long TotalInCents);

public sealed record DashboardDay(DateOnly Day, int Orders, long RevenueInCents);

public sealed record DashboardData(
    int OrdersCount,
    int CancelledCount,
    long RevenueInCents,
    long PaidInCents,
    IReadOnlyList<DashboardStatusCount> ByStatus,
    IReadOnlyList<DashboardPaymentTotal> ByPayment,
    IReadOnlyList<DashboardTopItem> TopItems,
    IReadOnlyList<DashboardDay> ByDay);

public interface IReportQueries
{
    Task<DashboardData> GetDashboardAsync(Guid establishmentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Speech to text. Runs on a local whisper server: it only listens, it never decides anything.</summary>
public interface ISpeechToText
{
    bool IsConfigured { get; }

    /// <summary>The words heard, or null when nothing could be understood or the server is out.</summary>
    Task<string?> TranscribeAsync(byte[] audio, string mime, CancellationToken cancellationToken);
}

/// <summary>Text to speech in Brazilian Portuguese, for answering a voice note with a voice note.</summary>
public interface ITextToSpeech
{
    bool IsConfigured { get; }

    /// <summary>A voice note in ogg/opus as base64, or null when it could not be made.</summary>
    Task<string?> SpeakAsync(string text, CancellationToken cancellationToken);
}
