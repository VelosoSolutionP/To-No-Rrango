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

public sealed record BrainBotDto(string Id, string Name, string? Profession, string? Photo, string? Persona, string? FirstMessage, int KnowledgeNodes);

public sealed record BrainDto(bool Configured, string? AttachedId, string? AttachedName, IReadOnlyList<BrainBotDto> Available, string? Problem);

public sealed record AttachBrainRequest(string BotId);

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

public sealed record InboundWhatsApp(string ExternalId, string Phone, string? PushName, string? Text, bool IsAudio);

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

            isAudio = message.TryGetProperty("audioMessage", out _);
        }

        if (string.IsNullOrWhiteSpace(text) && !isAudio)
        {
            return null;
        }

        var pushName = data.TryGetProperty("pushName", out var name) ? name.GetString() : null;

        return new InboundWhatsApp(id.GetString() ?? string.Empty, PhoneNumber.Digits(remote.Split('@')[0]), pushName, text, isAudio);
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
    IHotelClient hotel,
    IWhatsAppSender sender,
    IModuleAccess modules,
    ISecretProtector protector,
    IAppUrls urls,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly TimeSpan HandoffPause = TimeSpan.FromHours(2);
    private const int LoopWindowSeconds = 60;
    private const int MaxRepliesInLoopWindow = 10;
    private const string AudioNotSupported = "Ainda não consigo ouvir áudios por aqui. Pode me mandar por texto? Se preferir, escreva *atendente*.";

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

        if (settings.HotelBotId is null)
        {
            return "Escolha uma IA homologada no Hotel dos Bots antes de ativar o atendimento.";
        }

        try
        {
            var (connected, _) = await sender.GetStateAsync(settings.InstanceName, cancellationToken);

            if (!connected)
            {
                return "O WhatsApp não está conectado.";
            }

            var bots = await hotel.ListAvailableAsync(cancellationToken);

            return bots.Any(bot => bot.Id == settings.HotelBotId) ? null : "A IA escolhida não está mais homologada no Hotel dos Bots.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return "Não consegui confirmar a conexão e a IA. Tente novamente.";
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

    public async Task<Result<BrainDto>> GetBrainAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        if (await establishments.GetByIdAsync(establishmentId, cancellationToken) is null)
        {
            return Result<BrainDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        if (!hotel.IsConfigured)
        {
            return new BrainDto(false, settings?.HotelBotId, settings?.HotelBotName, [], "A conexão com o Hotel dos Bots ainda não foi configurada.");
        }

        try
        {
            var bots = await hotel.ListAvailableAsync(cancellationToken);
            var list = bots.Select(bot => new BrainBotDto(bot.Id, bot.Name, bot.Profession, bot.Photo, bot.Persona, bot.FirstMessage, bot.KnowledgeNodes)).ToList();

            return new BrainDto(true, settings?.HotelBotId, settings?.HotelBotName, list, list.Count == 0 ? "O Hotel ainda não tem IA homologada." : null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new BrainDto(true, settings?.HotelBotId, settings?.HotelBotName, [], "Não consegui falar com o Hotel dos Bots.");
        }
    }

    public Task<Result<BrainDto>> AttachBrainAsync(Guid establishmentId, string botId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<BrainDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<BrainDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            if (!hotel.IsConfigured)
            {
                return Result<BrainDto>.Failure(AppErrors.Validation("A conexão com o Hotel dos Bots ainda não foi configurada."));
            }

            // Checked against the Hotel at the moment of choosing, so an unapproved bot can never take over.
            var bot = (await hotel.ListAvailableAsync(cancellationToken)).FirstOrDefault(item => item.Id == botId);

            if (bot is null)
            {
                return Result<BrainDto>.Failure(AppErrors.Validation("Esta IA não está homologada no Hotel dos Bots."));
            }

            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (settings is null)
            {
                var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
                settings = new WhatsAppSettings(establishment.TenantId, establishment.Id, protector.Protect(token), clock.UtcNow);
                settingsRepository.Add(settings);
            }

            settings.AttachBrain(bot.Id, bot.Name, bot.Prompt, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetBrainAsync(establishmentId, cancellationToken);
        });

    public Task<Result<BrainDto>> DetachBrainAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<BrainDto>(async () =>
        {
            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            settings?.DetachBrain(clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetBrainAsync(establishmentId, cancellationToken);
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

            var now = clock.UtcNow;
            var key = PhoneNumber.Key(inbound.Phone);
            var conversation = await conversations.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId && item.PhoneKey == key, cancellationToken);

            if (conversation is null)
            {
                conversation = new Conversation(settings.TenantId, establishmentId, inbound.Phone, inbound.PushName, now);
                conversations.Add(conversation);
            }

            var text = string.IsNullOrWhiteSpace(inbound.Text) ? "[áudio]" : inbound.Text.Trim();
            text = text.Length > 4000 ? text[..4000] : text;

            messages.Add(new ConversationMessage(settings.TenantId, establishmentId, conversation.Id, MessageDirection.Inbound, MessageAuthor.Customer, text, inbound.ExternalId, now));
            conversation.Touch(text, now);

            if (settings.AutoReply
                && sender.IsConfigured
                && settings.IsReady
                && !conversation.IsBotPaused(now)
                && await modules.IsAllowedAsync(settings.TenantId, Modules.Ia, cancellationToken))
            {
                if (inbound.IsAudio)
                {
                    AddReply(conversation, MessageAuthor.Zest, AudioNotSupported, now);
                }
                else
                {
                    await ReplyWithZestAsync(establishment, conversation, inbound, text, now, cancellationToken);
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        });

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

        if (answer.Intent == ZestIntent.Handoff)
        {
            conversation.SetSalesState(null);
            conversation.PauseBot(now.Add(HandoffPause));
        }
    }

    private ConversationMessage AddReply(Conversation conversation, MessageAuthor author, string text, DateTimeOffset now, string? imageBase64 = null)
    {
        var message = new ConversationMessage(conversation.TenantId, conversation.EstablishmentId, conversation.Id, MessageDirection.Outbound, author, text, null, now);
        messages.Add(message);
        outbox.Add(new OutboundMessage(conversation.TenantId, conversation.EstablishmentId, conversation.Phone, text, now, imageBase64));
        conversation.Touch(text, now);

        return message;
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
