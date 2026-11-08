using ToNoRango.Domain.Common;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Messaging;

public enum OutboundStatus
{
    Pending = 1,
    Sending = 2,
    Sent = 3,
    Failed = 4
}

public enum MessageDirection
{
    Inbound = 1,
    Outbound = 2
}

public enum MessageAuthor
{
    Customer = 1,
    Zest = 2,
    Staff = 3,
    System = 4
}

public sealed class WhatsAppSettings : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string? InstanceName { get; private set; }
    public string? EncryptedApiKey { get; private set; }
    public string EncryptedWebhookToken { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public bool NotifyOrderStatus { get; private set; }
    public bool AutoReply { get; private set; }
    public string? HotelBotId { get; private set; }
    public string? HotelBotName { get; private set; }
    public string? HotelBotPrompt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private WhatsAppSettings() { }

    public WhatsAppSettings(Guid tenantId, Guid establishmentId, string encryptedWebhookToken, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        EncryptedWebhookToken = Ensure.NotNullOrWhiteSpace(encryptedWebhookToken, nameof(EncryptedWebhookToken));
        UpdatedAt = now;
    }

    public void AttachBrain(string botId, string botName, string prompt, DateTimeOffset now)
    {
        HotelBotId = Ensure.NotNullOrWhiteSpace(botId, nameof(botId));
        HotelBotName = botName;
        HotelBotPrompt = prompt;
        UpdatedAt = now;
    }

    public void DetachBrain(DateTimeOffset now)
    {
        HotelBotId = null;
        HotelBotName = null;
        HotelBotPrompt = null;
        UpdatedAt = now;
    }

    public void SetAutomation(bool enabled, DateTimeOffset now)
    {
        AutoReply = enabled;
        NotifyOrderStatus = enabled;
        UpdatedAt = now;
    }

    public void Disable(DateTimeOffset now)
    {
        IsEnabled = false;
        AutoReply = false;
        NotifyOrderStatus = false;
        UpdatedAt = now;
    }

    public bool IsReady => IsEnabled && InstanceName is not null && EncryptedApiKey is not null;

    public void Update(
        string? instanceName,
        string? encryptedApiKey,
        bool isEnabled,
        bool notifyOrderStatus,
        bool autoReply,
        DateTimeOffset now)
    {
        InstanceName = string.IsNullOrWhiteSpace(instanceName) ? InstanceName : Ensure.MaxLength(instanceName.Trim(), 80, nameof(InstanceName));
        EncryptedApiKey = encryptedApiKey ?? EncryptedApiKey;
        IsEnabled = isEnabled;
        NotifyOrderStatus = notifyOrderStatus;
        AutoReply = autoReply;
        UpdatedAt = now;
    }
}

/// <summary>Outbox row: written in the same transaction as the business change and sent by a worker.</summary>
public sealed class OutboundMessage : AggregateRoot
{
    public const int MaxAttempts = 5;

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string? ImageBase64 { get; private set; }
    public OutboundStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private OutboundMessage() { }

    public OutboundMessage(Guid tenantId, Guid establishmentId, string phone, string text, DateTimeOffset now, string? imageBase64 = null)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Phone = PhoneNumber.ToWhatsApp(Ensure.NotNullOrWhiteSpace(phone, nameof(Phone)));
        Text = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(text, nameof(Text)), 4000, nameof(Text));
        ImageBase64 = imageBase64;
        Status = OutboundStatus.Pending;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = OutboundStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        LastError = error.Length > 300 ? error[..300] : error;

        if (Attempts >= MaxAttempts)
        {
            Status = OutboundStatus.Failed;
            return;
        }

        Status = OutboundStatus.Pending;
        NextAttemptAt = now.AddSeconds(Math.Pow(4, Attempts) * 5);
    }
}

public sealed class Conversation : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string PhoneKey { get; private set; } = string.Empty;
    public string? CustomerName { get; private set; }
    public string? LastMessage { get; private set; }
    public DateTimeOffset LastMessageAt { get; private set; }
    public DateTimeOffset? BotPausedUntil { get; private set; }
    public string? SalesState { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Conversation() { }

    public Conversation(Guid tenantId, Guid establishmentId, string phone, string? customerName, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Phone = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(phone, nameof(Phone)), 30, nameof(Phone));
        PhoneKey = PhoneNumber.Key(phone);
        CustomerName = customerName;
        LastMessageAt = now;
        CreatedAt = now;
    }

    public bool IsBotPaused(DateTimeOffset now) => BotPausedUntil is not null && BotPausedUntil > now;

    public void PauseBot(DateTimeOffset until) => BotPausedUntil = until;

    public void ResumeBot() => BotPausedUntil = null;

    public void SetSalesState(string? salesState) => SalesState = salesState;

    public void Touch(string text, DateTimeOffset now)
    {
        LastMessage = text.Length > 200 ? text[..200] : text;
        LastMessageAt = now;
    }
}

public sealed class ConversationMessage : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid ConversationId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public MessageAuthor Author { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public string? ExternalId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ConversationMessage() { }

    public ConversationMessage(
        Guid tenantId,
        Guid establishmentId,
        Guid conversationId,
        MessageDirection direction,
        MessageAuthor author,
        string text,
        string? externalId,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        ConversationId = Ensure.NotEmpty(conversationId, nameof(ConversationId));
        Direction = direction;
        Author = author;
        Text = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(text, nameof(Text)), 4000, nameof(Text));
        ExternalId = externalId;
        CreatedAt = now;
    }
}
