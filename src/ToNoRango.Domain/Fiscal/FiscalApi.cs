using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Fiscal;

/// <summary>
/// Key a partner system (another ZEST product, a software house, an accountant tool) uses to call the
/// fiscal API of one store. Only the SHA-256 of the key is kept; the key itself is shown once.
/// A test key never reaches the SEFAZ: its notes are simulated and kept apart from the books.
/// </summary>
public sealed class FiscalApiKey : AggregateRoot
{
    public const string LivePrefix = "zf_live_";
    public const string TestPrefix = "zf_test_";

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>The first characters of the key, enough to recognise it in lists and logs.</summary>
    public string Prefix { get; private set; } = string.Empty;
    public string Hash { get; private set; } = string.Empty;
    public bool IsTest { get; private set; }

    /// <summary>False: the key only reads (notes, XML, books). True: it also issues, cancels and corrects.</summary>
    public bool CanIssue { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private FiscalApiKey() { }

    public FiscalApiKey(Guid tenantId, Guid establishmentId, string name, string prefix, string hash, bool isTest, bool canIssue, string createdBy, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(name)).Trim(), 60, nameof(name));
        Prefix = Ensure.NotNullOrWhiteSpace(prefix, nameof(prefix));
        Hash = Ensure.NotNullOrWhiteSpace(hash, nameof(hash));
        IsTest = isTest;
        CanIssue = canIssue;
        CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "desconhecido" : createdBy.Trim()[..Math.Min(createdBy.Trim().Length, 160)];
        CreatedAt = now;
    }

    public bool IsActive => RevokedAt is null;

    public void Touch(DateTimeOffset now)
    {
        // One write per minute is enough to show "last used"; every call would hammer the row.
        if (LastUsedAt is null || now - LastUsedAt.Value > TimeSpan.FromMinutes(1))
        {
            LastUsedAt = now;
        }
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>Where a store's fiscal events are pushed. Each delivery is signed with the webhook secret.</summary>
public sealed class FiscalWebhook : AggregateRoot
{
    public const string NoteAuthorized = "note.authorized";
    public const string NoteRejected = "note.rejected";
    public const string NoteCancelled = "note.cancelled";
    public const string NoteCorrected = "note.corrected";
    public const string NoteContingency = "note.contingency";
    public const string Ping = "ping";

    public static readonly IReadOnlyList<string> Events = [NoteAuthorized, NoteRejected, NoteCancelled, NoteCorrected, NoteContingency];

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string EncryptedSecret { get; private set; } = string.Empty;

    /// <summary>Comma separated list of <see cref="Events"/>.</summary>
    public string Subscribed { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    /// <summary>Notes changed after this moment have not been turned into events yet.</summary>
    public DateTimeOffset ScannedUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private FiscalWebhook() { }

    public FiscalWebhook(Guid tenantId, Guid establishmentId, string url, string encryptedSecret, IEnumerable<string> events, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Url = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(url, nameof(url)).Trim(), 500, nameof(url));
        EncryptedSecret = Ensure.NotNullOrWhiteSpace(encryptedSecret, nameof(encryptedSecret));
        SetEvents(events);
        IsActive = true;
        ScannedUntil = now;
        CreatedAt = now;
    }

    public IReadOnlyList<string> SubscribedEvents => Subscribed.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public bool Wants(string eventName) => eventName == Ping || SubscribedEvents.Contains(eventName);

    public void SetEvents(IEnumerable<string> events)
    {
        var chosen = events.Where(Events.Contains).Distinct().ToList();

        if (chosen.Count == 0)
        {
            throw new ArgumentException("Escolha pelo menos um evento.", nameof(events));
        }

        Subscribed = string.Join(',', chosen);
    }

    public void Advance(DateTimeOffset until)
    {
        if (until > ScannedUntil)
        {
            ScannedUntil = until;
        }
    }

    public void Deactivate() => IsActive = false;
}

public enum WebhookDeliveryStatus
{
    Pending = 1,
    Delivered = 2,
    Failed = 3
}

/// <summary>One event for one webhook. The key makes each event unique, so a rescan never sends twice.</summary>
public sealed class FiscalWebhookDelivery : AggregateRoot
{
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromHours(24)
    ];

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid WebhookId { get; private set; }
    public string EventKey { get; private set; } = string.Empty;
    public string Event { get; private set; } = string.Empty;
    public Guid? DocumentId { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public WebhookDeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public int? LastStatusCode { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    private FiscalWebhookDelivery() { }

    public FiscalWebhookDelivery(Guid id, FiscalWebhook webhook, string eventKey, string eventName, Guid? documentId, string payload, DateTimeOffset now)
    {
        Id = id;
        TenantId = webhook.TenantId;
        EstablishmentId = webhook.EstablishmentId;
        WebhookId = webhook.Id;
        EventKey = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(eventKey, nameof(eventKey)), 120, nameof(eventKey));
        Event = Ensure.NotNullOrWhiteSpace(eventName, nameof(eventName));
        DocumentId = documentId;
        Payload = Ensure.NotNullOrWhiteSpace(payload, nameof(payload));
        Status = WebhookDeliveryStatus.Pending;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    public void Succeed(int statusCode, DateTimeOffset now)
    {
        Attempts++;
        Status = WebhookDeliveryStatus.Delivered;
        LastStatusCode = statusCode;
        LastError = null;
        DeliveredAt = now;
    }

    public void Fail(int? statusCode, string error, DateTimeOffset now)
    {
        Attempts++;
        LastStatusCode = statusCode;
        LastError = error.Length <= 500 ? error : error[..500];

        if (Attempts > Backoff.Length)
        {
            Status = WebhookDeliveryStatus.Failed;
            return;
        }

        NextAttemptAt = now + Backoff[Attempts - 1];
    }

    /// <summary>Gives up without another attempt (the webhook no longer exists).</summary>
    public void Abandon(string error)
    {
        Status = WebhookDeliveryStatus.Failed;
        LastError = error;
    }

    /// <summary>Sends again now, by hand, with a fresh set of attempts.</summary>
    public void Retry(DateTimeOffset now)
    {
        Status = WebhookDeliveryStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }
}
