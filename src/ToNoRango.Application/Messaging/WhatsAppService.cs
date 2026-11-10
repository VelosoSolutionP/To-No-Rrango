using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Zest;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Messaging;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Messaging;

public sealed record WhatsAppSettingsRequest(
    string? InstanceName,
    string? ApiKey,
    bool IsEnabled,
    bool NotifyOrderStatus,
    bool AutoReply);

public sealed record WhatsAppSettingsDto(
    string? InstanceName,
    bool HasApiKey,
    bool IsEnabled,
    bool NotifyOrderStatus,
    bool AutoReply,
    bool ServerConfigured,
    string? WebhookUrl);

public sealed record WhatsAppConnectionDto(bool Connected, string? QrCodeBase64, bool CanProvision, string? Problem, string? PairingCode = null, string? Number = null);

public sealed record SetAutomationRequest(bool Enabled);

public sealed record ConnectWhatsAppRequest(string? Phone);

public sealed record ConversationDto(
    Guid Id,
    string Phone,
    string? CustomerName,
    string? LastMessage,
    DateTimeOffset LastMessageAt,
    bool IsBotPaused);

public sealed record MessageDto(Guid Id, MessageDirection Direction, MessageAuthor Author, string Text, DateTimeOffset CreatedAt);

public sealed record StaffMessageRequest(string Text);

public sealed record SetBotRequest(bool Paused);

/// <summary>A customer message relayed by ZEST. Audio goes as base64 (ogg/opus from WhatsApp, or any format ffmpeg reads).</summary>
public sealed record ConnectorMessageRequest(string Phone, string? Name, string? Text, string? AudioBase64, string? AudioMime, string? MessageId);

/// <summary>One reply to deliver: text, an image (Pix QR Code) or a voice note (ogg/opus).</summary>
public sealed record ConnectorReply(string? Text, string? ImageBase64, string? AudioBase64);

public sealed record ConnectorReplyDto(IReadOnlyList<ConnectorReply> Replies, bool Handoff, bool Duplicate = false);

public sealed record InboundWhatsApp(string ExternalId, string Phone, string? PushName, string? Text, bool IsAudio, string? AudioBase64 = null, string? AudioMime = null);

public static class EvolutionWebhookParser
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    /// <summary>Returns the customer message, or null for groups, echoes and history replays.</summary>
    public static InboundWhatsApp? Parse(JsonElement payload, DateTimeOffset now)
    {
        if (!payload.TryGetProperty("event", out var eventName)
            || !string.Equals(eventName.GetString()?.Replace('_', '.'), "messages.upsert", StringComparison.OrdinalIgnoreCase)
            || !payload.TryGetProperty("data", out var data)
            || !data.TryGetProperty("key", out var key)
            || !key.TryGetProperty("remoteJid", out var jid)
            || !key.TryGetProperty("id", out var id))
        {
            return null;
        }

        var remote = jid.GetString() ?? string.Empty;

        // Newer WhatsApp accounts arrive with an opaque @lid; the real number comes in a separate field.
        if (remote.EndsWith("@lid", StringComparison.Ordinal))
        {
            remote = new[] { "remoteJidAlt", "senderPn" }
                .Select(field => key.TryGetProperty(field, out var alternative) ? alternative.GetString() : null)
                .FirstOrDefault(value => value?.EndsWith("@s.whatsapp.net", StringComparison.Ordinal) == true) ?? remote;
        }

        if (!remote.EndsWith("@s.whatsapp.net", StringComparison.Ordinal)
            || (key.TryGetProperty("fromMe", out var fromMe) && fromMe.ValueKind == JsonValueKind.True))
        {
            return null;
        }

        if (data.TryGetProperty("messageTimestamp", out var timestamp)
            && timestamp.ValueKind == JsonValueKind.Number
            && timestamp.TryGetInt64(out var seconds)
            && now - DateTimeOffset.FromUnixTimeSeconds(seconds) > MaxAge)
        {
            return null;
        }

        string? text = null;
        var isAudio = false;
        string? audioBase64 = null;
        string? audioMime = null;

        if (data.TryGetProperty("message", out var message))
        {
            // Chats with disappearing messages wrap the content one level deeper.
            if (message.TryGetProperty("ephemeralMessage", out var ephemeral) && ephemeral.TryGetProperty("message", out var inner))
            {
                message = inner;
            }

            if (message.TryGetProperty("conversation", out var conversation))
            {
                text = conversation.GetString();
            }
            else if (message.TryGetProperty("extendedTextMessage", out var extended) && extended.TryGetProperty("text", out var extendedText))
            {
                text = extendedText.GetString();
            }

            isAudio = message.TryGetProperty("audioMessage", out var audioMessage);

            // With "webhook base64" on, Evolution already sends the audio; otherwise it is downloaded later.
            if (isAudio && message.TryGetProperty("base64", out var inlined) && inlined.ValueKind == JsonValueKind.String)
            {
                audioBase64 = inlined.GetString();
                audioMime = audioMessage.TryGetProperty("mimetype", out var mimetype) && mimetype.ValueKind == JsonValueKind.String ? mimetype.GetString() : null;
            }
        }

        if (string.IsNullOrWhiteSpace(text) && !isAudio)
        {
            return null;
        }

        var pushName = data.TryGetProperty("pushName", out var name) ? name.GetString() : null;

        return new InboundWhatsApp(id.GetString() ?? string.Empty, PhoneNumber.Digits(remote.Split('@')[0]), pushName, text, isAudio, audioBase64, audioMime);
    }
}

public sealed class WhatsAppService(
    IRepository<WhatsAppSettings> settingsRepository,
    IRepository<Conversation> conversations,
    IRepository<ConversationMessage> messages,
    IRepository<OutboundMessage> outbox,
    IEstablishmentRepository establishments,
    ZestService zest,
    ZestSalesService sales,
    IWhatsAppSender sender,
    IModuleAccess modules,
    ISecretProtector protector,
    IAppUrls urls,
    IUnitOfWork unitOfWork,
    IClock clock,
    ISpeechToText listener,
    ITextToSpeech voice)
{
    private static readonly TimeSpan HandoffPause = TimeSpan.FromHours(2);
    private const int LoopWindowSeconds = 60;
    private const int MaxRepliesInLoopWindow = 10;
    private const string AudioNotSupported = "Ainda não consigo ouvir áudios por aqui. Pode me mandar por texto? Se preferir, escreva *atendente*.";
    private const string AudioNotUnderstood = "Não consegui entender o seu áudio. Pode repetir mais perto do microfone ou mandar por texto? Se preferir, escreva *atendente*.";

    public async Task<Result<WhatsAppSettingsDto>> GetSettingsAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<WhatsAppSettingsDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        return ToDto(establishmentId, settings);
    }

    public Task<Result<WhatsAppSettingsDto>> SaveSettingsAsync(Guid establishmentId, WhatsAppSettingsRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<WhatsAppSettingsDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<WhatsAppSettingsDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (settings is null)
            {
                var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
                settings = new WhatsAppSettings(establishment.TenantId, establishment.Id, protector.Protect(token), clock.UtcNow);
                settingsRepository.Add(settings);
            }

            settings.Update(
                request.InstanceName,
                string.IsNullOrWhiteSpace(request.ApiKey) ? null : protector.Protect(request.ApiKey.Trim()),
                request.IsEnabled,
                // Automation is only switched on by SetAutomationAsync, after its safety checks.
                request.NotifyOrderStatus && settings.NotifyOrderStatus,
                request.AutoReply && settings.AutoReply,
                clock.UtcNow);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(establishmentId, settings);
        });

    /// <summary>One click for the establishment: creates the instance, points it at this API and returns the QR Code.</summary>
    public Task<Result<WhatsAppConnectionDto>> ConnectAsync(Guid establishmentId, string? phone, CancellationToken cancellationToken) =>
        Guarded.RunAsync<WhatsAppConnectionDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<WhatsAppConnectionDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var number = string.IsNullOrWhiteSpace(phone) ? null : PhoneNumber.ToWhatsApp(phone);

            if (number is not null && number.Length is < 12 or > 13)
            {
                return Result<WhatsAppConnectionDto>.Failure(AppErrors.Validation("Informe o número com DDD."));
            }

            if (!sender.CanProvision)
            {
                return new WhatsAppConnectionDto(false, null, false, "O servidor de WhatsApp ainda não foi configurado na plataforma.");
            }

            if (urls.WebhookBaseUrl is null)
            {
                return new WhatsAppConnectionDto(false, null, true, "Falta configurar o endereço público da API para receber as mensagens.");
            }

            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (settings is null)
            {
                var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
                settings = new WhatsAppSettings(establishment.TenantId, establishment.Id, protector.Protect(token), clock.UtcNow);
                settingsRepository.Add(settings);
            }

            var instance = settings.InstanceName ?? $"tnr-{establishmentId:N}";

            try
            {
                await sender.EnsureInstanceAsync(instance, cancellationToken);
                await sender.SetWebhookAsync(
                    instance,
                    $"{urls.WebhookBaseUrl}/api/webhooks/evolution/{establishmentId}/{protector.Unprotect(settings.EncryptedWebhookToken)}",
                    cancellationToken);

                settings.Update(instance, protector.Protect(sender.PlatformApiKey!), true, settings.NotifyOrderStatus, settings.AutoReply, clock.UtcNow);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                var (qr, pairing) = await sender.GetConnectCodesAsync(instance, number, cancellationToken);

                return new WhatsAppConnectionDto(qr is null && pairing is null, qr, true, null, pairing);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return Result<WhatsAppConnectionDto>.Failure(Error.External("whatsapp.unavailable", "O servidor de WhatsApp não respondeu. Tente novamente."));
            }
        });

    public async Task<Result<WhatsAppConnectionDto>> GetConnectionAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        if (settings?.InstanceName is null || !sender.CanProvision)
        {
            return new WhatsAppConnectionDto(false, null, sender.CanProvision, null);
        }

        try
        {
            var (connected, number) = await sender.GetStateAsync(settings.InstanceName, cancellationToken);

            return new WhatsAppConnectionDto(connected, null, true, null, null, number);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new WhatsAppConnectionDto(false, null, true, "O servidor de WhatsApp não respondeu.");
        }
    }

    /// <summary>Automatic replies reach real customers, so they start only on a connected number with an approved AI.</summary>
    public Task<Result<WhatsAppSettingsDto>> SetAutomationAsync(Guid establishmentId, bool enabled, CancellationToken cancellationToken) =>
        Guarded.RunAsync<WhatsAppSettingsDto>(async () =>
        {
            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (settings is null)
            {
                return Result<WhatsAppSettingsDto>.Failure(AppErrors.Validation("Conecte um WhatsApp antes de ativar o atendimento."));
            }

            if (enabled)
            {
                var blocked = await AutomationBlockAsync(settings, cancellationToken);

                if (blocked is not null)
                {
                    return Result<WhatsAppSettingsDto>.Failure(AppErrors.Validation(blocked));
                }
            }

            settings.SetAutomation(enabled, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(establishmentId, settings);
        });

    private async Task<string?> AutomationBlockAsync(WhatsAppSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsReady || settings.InstanceName is null)
        {
            return "Conecte um WhatsApp antes de ativar o atendimento.";
        }

        try
        {
            var (connected, _) = await sender.GetStateAsync(settings.InstanceName, cancellationToken);

            if (!connected)
            {
                    return "O WhatsApp não está conectado.";
            }

            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return "Não consegui confirmar a conexão. Tente novamente.";
        }
    }

    public Task<Result<WhatsAppConnectionDto>> DisconnectAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<WhatsAppConnectionDto>(async () =>
        {
            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (settings?.InstanceName is null)
            {
                return Result<WhatsAppConnectionDto>.Failure(AppErrors.NotFound("whatsapp.not_connected", "Nenhum WhatsApp conectado."));
            }

            try
            {
                await sender.LogoutAsync(settings.InstanceName, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return Result<WhatsAppConnectionDto>.Failure(Error.External("whatsapp.unavailable", "O servidor de WhatsApp não respondeu. Tente novamente."));
            }

            settings.Disable(clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new WhatsAppConnectionDto(false, null, true, null);
        });

    public async Task<Result<IReadOnlyList<ConversationDto>>> ListConversationsAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await conversations.ListAsync(
            conversation => conversation.EstablishmentId == establishmentId,
            conversation => conversation.LastMessageAt,
            cancellationToken,
            descending: true,
            take: 100);

        return list.Select(ToDto).ToList();
    }

    public async Task<Result<IReadOnlyList<MessageDto>>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var list = await messages.ListAsync(
            message => message.ConversationId == conversationId,
            message => message.CreatedAt,
            cancellationToken,
            descending: true,
            take: 200);

        return list.Reverse().Select(ToDto).ToList();
    }

    /// <summary>A staff reply pauses the bot for that customer, so the two do not talk over each other.</summary>
    public Task<Result<MessageDto>> SendStaffMessageAsync(Guid conversationId, StaffMessageRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<MessageDto>(async () =>
        {
            var conversation = await conversations.FindAsync(conversationId, cancellationToken);

            if (conversation is null)
            {
                return Result<MessageDto>.Failure(AppErrors.NotFound("conversation.not_found", "Conversa não encontrada."));
            }

            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == conversation.EstablishmentId, cancellationToken);

            if (settings is not { IsReady: true } || !sender.IsConfigured)
            {
                return Result<MessageDto>.Failure(AppErrors.Validation("O WhatsApp deste estabelecimento não está configurado."));
            }

            var now = clock.UtcNow;
            conversation.PauseBot(now.Add(HandoffPause));
            var message = AddReply(conversation, MessageAuthor.Staff, request.Text, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(message);
        });

    public Task<Result<ConversationDto>> SetBotAsync(Guid conversationId, bool paused, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ConversationDto>(async () =>
        {
            var conversation = await conversations.FindAsync(conversationId, cancellationToken);

            if (conversation is null)
            {
                return Result<ConversationDto>.Failure(AppErrors.NotFound("conversation.not_found", "Conversa não encontrada."));
            }

            if (paused)
            {
                conversation.PauseBot(clock.UtcNow.Add(HandoffPause));
            }
            else
            {
                conversation.ResumeBot();
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(conversation);
        });

    public Task<Result<bool>> HandleWebhookAsync(Guid establishmentId, string token, JsonElement payload, CancellationToken cancellationToken) =>
        Guarded.RunAsync<bool>(async () =>
        {
            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (settings is null || !TokenMatches(settings, token))
            {
                return Result<bool>.Failure(Error.Unauthorized("webhook.token", "Token inválido."));
            }

            var inbound = EvolutionWebhookParser.Parse(payload, clock.UtcNow);

            if (inbound is null
                || !settings.IsEnabled
                || !await modules.IsAllowedAsync(settings.TenantId, Modules.WhatsApp, cancellationToken))
            {
                return true;
            }

            if (await messages.AnyAsync(message => message.EstablishmentId == establishmentId && message.ExternalId == inbound.ExternalId, cancellationToken))
            {
                return true;
            }

            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<bool>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var autoReply = settings.AutoReply && sender.IsConfigured && settings.IsReady;
            await ProcessInboundAsync(establishment, settings, inbound, autoReply, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        });

    /// <summary>
    /// ZEST relays a customer message (text or audio) and gets the replies back instead of them
    /// being sent through Evolution. The conversation, sale and history are the same as WhatsApp.
    /// </summary>
    public Task<Result<ConnectorReplyDto>> RelayFromZestAsync(Establishment establishment, ConnectorMessageRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ConnectorReplyDto>(async () =>
        {
            var phone = PhoneNumber.Digits(request.Phone);

            if (phone.Length < 10)
            {
                return Result<ConnectorReplyDto>.Failure(AppErrors.Validation("Telefone do cliente inválido."));
            }

            if (string.IsNullOrWhiteSpace(request.Text) && string.IsNullOrWhiteSpace(request.AudioBase64))
            {
                return Result<ConnectorReplyDto>.Failure(AppErrors.Validation("Envie texto ou áudio."));
            }

            var externalId = string.IsNullOrWhiteSpace(request.MessageId) ? $"zest-{Guid.NewGuid():N}" : $"zest-{request.MessageId.Trim()}";
            externalId = externalId.Length > 120 ? externalId[..120] : externalId;

            if (await messages.AnyAsync(message => message.EstablishmentId == establishment.Id && message.ExternalId == externalId, cancellationToken))
            {
                return new ConnectorReplyDto([], false, Duplicate: true);
            }

            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishment.Id, cancellationToken);
            var inbound = new InboundWhatsApp(
                externalId,
                phone,
                request.Name,
                request.Text,
                !string.IsNullOrWhiteSpace(request.AudioBase64),
                request.AudioBase64,
                request.AudioMime);

            _collected = [];
            var handoff = await ProcessInboundAsync(establishment, settings, inbound, true, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var replies = _collected;
            _collected = null;
            return new ConnectorReplyDto(replies, handoff);
        });

    /// <summary>Stores the customer message, transcribes audio and answers with the sale or the store's knowledge. Returns true when the bot handed over to a person.</summary>
    private async Task<bool> ProcessInboundAsync(Establishment establishment, WhatsAppSettings? settings, InboundWhatsApp inbound, bool autoReply, CancellationToken cancellationToken)
    {
        var establishmentId = establishment.Id;
        var tenantId = establishment.TenantId;
            var now = clock.UtcNow;
            var key = PhoneNumber.Key(inbound.Phone);
            var conversation = await conversations.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId && item.PhoneKey == key, cancellationToken);

            if (conversation is null)
            {
                conversation = new Conversation(tenantId, establishmentId, inbound.Phone, inbound.PushName, now);
                conversations.Add(conversation);
            }

            var heard = inbound.IsAudio ? await TranscribeAsync(settings, inbound, cancellationToken) : null;
            var text = heard is not null
                ? $"🎤 {heard}"
                : string.IsNullOrWhiteSpace(inbound.Text) ? "[áudio]" : inbound.Text.Trim();
            text = text.Length > 4000 ? text[..4000] : text;

            messages.Add(new ConversationMessage(tenantId, establishmentId, conversation.Id, MessageDirection.Inbound, MessageAuthor.Customer, text, inbound.ExternalId, now));
            conversation.Touch(text, now);

            if (autoReply
                && !conversation.IsBotPaused(now)
                && await modules.IsAllowedAsync(tenantId, Modules.Ia, cancellationToken))
            {
                if (inbound.IsAudio && heard is null)
                {
                    AddReply(conversation, MessageAuthor.Zest, listener.IsConfigured ? AudioNotUnderstood : AudioNotSupported, now);
                }
                else if (heard is not null)
                {
                    // The customer sees what was understood, so a wrong word is caught before the order closes.
                    _replyVoice = true;
                    _replyPrefix = $"🎤 _Entendi: \"{(heard.Length > 200 ? heard[..200] + "…" : heard)}\"_\n\n";
                    await ReplyWithZestAsync(establishment, conversation, inbound, heard, now, cancellationToken);
                    await SpeakRepliesAsync(conversation, now, cancellationToken);
                }
                else
                {
                    await ReplyWithZestAsync(establishment, conversation, inbound, text, now, cancellationToken);
                }
            }

        return conversation.IsBotPaused(now);
    }

    /// <summary>A sale takes priority; only a message outside a sale is answered as a question.</summary>
    private async Task ReplyWithZestAsync(
        Establishment establishment,
        Conversation conversation,
        InboundWhatsApp inbound,
        string text,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Two automatic attendants can answer each other forever; a burst like this is never a person.
        var since = now.AddSeconds(-LoopWindowSeconds);
        var recentReplies = await messages.CountAsync(
            message => message.ConversationId == conversation.Id && message.Author == MessageAuthor.Zest && message.CreatedAt >= since,
            cancellationToken);

        if (recentReplies >= MaxRepliesInLoopWindow)
        {
            conversation.PauseBot(now.Add(HandoffPause));
            return;
        }

        if (!ZestBrain.IsHandoff(text) && await sales.CanSellAsync(establishment.TenantId, cancellationToken))
        {
            var state = sales.Restore(conversation.SalesState);
            var idempotency = $"zest-{conversation.Id:N}-{inbound.ExternalId}";
            var result = await sales.HandleAsync(establishment, state, inbound.Phone, text, inbound.PushName, idempotency[..Math.Min(idempotency.Length, 100)], cancellationToken);

            if (result is not null)
            {
                conversation.SetSalesState(result.State.Step == SalesStep.Idle ? null : ZestSalesService.Serialize(result.State));
                AddReply(conversation, MessageAuthor.Zest, result.Reply.Text, now, result.Reply.ImageBase64);

                if (result.Reply.FollowUp is not null)
                {
                    AddReply(conversation, MessageAuthor.Zest, result.Reply.FollowUp, now.AddMilliseconds(1));
                }

                return;
            }
        }

        var answer = await zest.AnswerAsync(establishment, inbound.Phone, text, cancellationToken);
        var previous = await messages.ListAsync(
            message => message.ConversationId == conversation.Id && message.Author == MessageAuthor.Zest,
            message => message.CreatedAt,
            cancellationToken,
            descending: true,
            take: 1);

        // Repeating the same sentence helps no one and is what keeps a loop with another bot alive.
        if (previous.Count == 1 && previous[0].Text == answer.Text)
        {
            return;
        }

        AddReply(conversation, MessageAuthor.Zest, answer.Text, now);

        if (answer.Intent == ZestIntent.Unknown)
        {
            await zest.RecordGapAsync(establishment, text, "whatsapp", cancellationToken);
        }

        if (answer.Intent == ZestIntent.Handoff)
        {
            conversation.SetSalesState(null);
            conversation.PauseBot(now.Add(HandoffPause));
        }
    }

    private List<ConnectorReply>? _collected;
    private bool _replyVoice;
    private string? _replyPrefix;
    private readonly List<string> _toSpeak = [];

    private async Task SpeakRepliesAsync(Conversation conversation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var spoken = string.Join("\n\n", _toSpeak);
        _toSpeak.Clear();
        _replyVoice = false;

        if (spoken.Length == 0)
        {
            return;
        }

        var audio = await voice.SpeakAsync(spoken, cancellationToken);

        if (audio is not null && _collected is not null)
        {
            _collected.Add(new ConnectorReply(null, null, audio));
        }
        else if (audio is not null)
        {
            outbox.Add(new OutboundMessage(conversation.TenantId, conversation.EstablishmentId, conversation.Phone, string.Empty, now.AddMilliseconds(5), audioBase64: audio, audioMimeType: "audio/ogg; codecs=opus"));
        }
    }

    private ConversationMessage AddReply(Conversation conversation, MessageAuthor author, string text, DateTimeOffset now, string? imageBase64 = null)
    {
        if (_replyPrefix is not null && author == MessageAuthor.Zest)
        {
            text = _replyPrefix + text;
            _replyPrefix = null;
        }

        var message = new ConversationMessage(conversation.TenantId, conversation.EstablishmentId, conversation.Id, MessageDirection.Outbound, author, text, null, now);
        messages.Add(message);

        if (_collected is not null)
        {
            _collected.Add(new ConnectorReply(text, imageBase64, null));
        }
        else
        {
            outbox.Add(new OutboundMessage(conversation.TenantId, conversation.EstablishmentId, conversation.Phone, text, now, imageBase64));
        }
        conversation.Touch(text, now);

        // Whoever spoke gets an answer they can listen to; the text stays for links, codes and totals.
        if (_replyVoice && author == MessageAuthor.Zest && voice.IsConfigured)
        {
            _toSpeak.Add(text);
        }

        return message;
    }

    /// <summary>Gets the words of a voice note. Null when there is no transcription server or nothing was understood.</summary>
    private async Task<string?> TranscribeAsync(WhatsAppSettings? settings, InboundWhatsApp inbound, CancellationToken cancellationToken)
    {
        if (!listener.IsConfigured)
        {
            return null;
        }

        var base64 = inbound.AudioBase64;
        var mime = inbound.AudioMime ?? "audio/ogg";

        if (base64 is null && settings is { IsReady: true } && sender.IsConfigured)
        {
            try
            {
                var media = await sender.GetMediaAsync(settings.InstanceName!, protector.Unprotect(settings.EncryptedApiKey!), inbound.ExternalId, cancellationToken);
                base64 = media?.Base64;
                mime = media?.Mime ?? mime;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        if (base64 is null)
        {
            return null;
        }

        try
        {
            return await listener.TranscribeAsync(Convert.FromBase64String(base64), mime, cancellationToken);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private bool TokenMatches(WhatsAppSettings settings, string token) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(protector.Unprotect(settings.EncryptedWebhookToken)),
        Encoding.UTF8.GetBytes(token ?? string.Empty));

    private WhatsAppSettingsDto ToDto(Guid establishmentId, WhatsAppSettings? settings) => new(
        settings?.InstanceName,
        settings?.EncryptedApiKey is not null,
        settings?.IsEnabled ?? false,
        settings?.NotifyOrderStatus ?? false,
        settings?.AutoReply ?? false,
        sender.IsConfigured,
        settings is null || urls.PublicApiUrl is null
            ? null
            : $"{urls.PublicApiUrl}/api/webhooks/evolution/{establishmentId}/{protector.Unprotect(settings.EncryptedWebhookToken)}");

    private ConversationDto ToDto(Conversation conversation) => new(
        conversation.Id,
        conversation.Phone,
        conversation.CustomerName,
        conversation.LastMessage,
        conversation.LastMessageAt,
        conversation.IsBotPaused(clock.UtcNow));

    private static MessageDto ToDto(ConversationMessage message) => new(message.Id, message.Direction, message.Author, message.Text, message.CreatedAt);
}
