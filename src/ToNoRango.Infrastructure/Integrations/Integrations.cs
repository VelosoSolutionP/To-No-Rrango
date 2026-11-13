using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ToNoRango.Application.Abstractions;

namespace ToNoRango.Infrastructure.Integrations;

public sealed class AppUrls(IConfiguration configuration) : IAppUrls
{
    private readonly string _web = (configuration["App:PublicWebUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public string? PublicApiUrl { get; } = string.IsNullOrWhiteSpace(configuration["App:PublicApiUrl"])
        ? null
        : configuration["App:PublicApiUrl"]!.TrimEnd('/');

    public string? WebhookBaseUrl { get; } = string.IsNullOrWhiteSpace(configuration["Evolution:WebhookBaseUrl"])
        ? string.IsNullOrWhiteSpace(configuration["App:PublicApiUrl"]) ? null : configuration["App:PublicApiUrl"]!.TrimEnd('/')
        : configuration["Evolution:WebhookBaseUrl"]!.TrimEnd('/');

    public string TrackingUrl(string orderCode) => $"{_web}/pedido/{orderCode}";

    public string MenuUrl(string slug) => $"{_web}/menu/{slug}";

    public string CourierUrl(string token) => $"{_web}/entrega/{token}";

    public string AccountantPortalUrl() => $"{_web}/contador";
}

public sealed class RedisOrderEvents(IConnectionMultiplexer redis, ILogger<RedisOrderEvents> logger) : IOrderEvents
{
    private static RedisChannel ChannelOf(string code) => RedisChannel.Literal($"tonorango:orders:{code}");

    public async Task PublishAsync(string orderCode)
    {
        try
        {
            await redis.GetSubscriber().PublishAsync(ChannelOf(orderCode), "update");
        }
        catch (RedisException exception)
        {
            // The tracking page still refreshes on its timer, so a lost event must not fail the order.
            logger.LogWarning(exception, "Could not publish the update of order {OrderCode}", orderCode);
        }
    }

    public async IAsyncEnumerable<string> ListenAsync(
        string orderCode,
        TimeSpan heartbeat,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queue = await redis.GetSubscriber().SubscribeAsync(ChannelOf(orderCode));

        try
        {
            var read = queue.ReadAsync(cancellationToken).AsTask();

            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = Task.Delay(heartbeat, cancellationToken);
                var finished = await Task.WhenAny(read, delay);

                if (finished == read)
                {
                    await read;
                    yield return "update";
                    read = queue.ReadAsync(cancellationToken).AsTask();
                }
                else
                {
                    await delay;
                    yield return "ping";
                }
            }
        }
        finally
        {
            await queue.UnsubscribeAsync();
        }
    }
}

public sealed class MercadoPagoGateway(HttpClient http, ILogger<MercadoPagoGateway> logger) : IPaymentGateway
{
    public Task<GatewayPayment> CreatePixAsync(string accessToken, PixCharge charge, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["transaction_amount"] = charge.AmountInCents / 100m,
            ["description"] = charge.Description,
            ["payment_method_id"] = "pix",
            ["external_reference"] = charge.ExternalReference,
            ["payer"] = new JsonObject { ["email"] = charge.PayerEmail }
        };

        AddNotificationUrl(body, charge.NotificationUrl);

        return SendAsync(HttpMethod.Post, "v1/payments", accessToken, body, charge.IdempotencyKey, cancellationToken);
    }

    public Task<GatewayPayment> CreateCardAsync(string accessToken, CardCharge charge, CancellationToken cancellationToken)
    {
        var payer = new JsonObject { ["email"] = charge.PayerEmail };

        if (!string.IsNullOrWhiteSpace(charge.DocumentNumber))
        {
            payer["identification"] = new JsonObject { ["type"] = charge.DocumentType ?? "CPF", ["number"] = charge.DocumentNumber };
        }

        var body = new JsonObject
        {
            ["transaction_amount"] = charge.AmountInCents / 100m,
            ["description"] = charge.Description,
            ["token"] = charge.Token,
            ["installments"] = charge.Installments,
            ["payment_method_id"] = charge.PaymentMethodId,
            ["external_reference"] = charge.ExternalReference,
            ["payer"] = payer
        };

        if (!string.IsNullOrWhiteSpace(charge.IssuerId))
        {
            body["issuer_id"] = charge.IssuerId;
        }

        AddNotificationUrl(body, charge.NotificationUrl);

        return SendAsync(HttpMethod.Post, "v1/payments", accessToken, body, charge.IdempotencyKey, cancellationToken);
    }

    /// <summary>The Mercado Pago hosted page. Boleto and ATM are left out: a meal cannot wait days for the payment.</summary>
    public async Task<GatewayCheckout> CreateCheckoutAsync(string accessToken, CheckoutCharge charge, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["title"] = charge.Description,
                ["quantity"] = 1,
                ["unit_price"] = charge.AmountInCents / 100m,
                ["currency_id"] = "BRL"
            }),
            ["external_reference"] = charge.ExternalReference,
            ["back_urls"] = new JsonObject { ["success"] = charge.ReturnUrl, ["pending"] = charge.ReturnUrl, ["failure"] = charge.ReturnUrl },
            ["payment_methods"] = new JsonObject
            {
                ["excluded_payment_types"] = new JsonArray(new JsonObject { ["id"] = "ticket" }, new JsonObject { ["id"] = "atm" })
            },
            ["expires"] = true,
            ["expiration_date_to"] = charge.ExpiresAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)
        };

        // Mercado Pago refuses auto_return on an address that is not https, as in a local copy.
        if (charge.ReturnUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            body["auto_return"] = "approved";
        }

        AddNotificationUrl(body, charge.NotificationUrl);

        using var response = await ExchangeAsync(HttpMethod.Post, "checkout/preferences", accessToken, body, null, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var id = StringOf(document.RootElement, "id");
        var url = StringOf(document.RootElement, "init_point");

        return id is null || url is null
            ? throw new PaymentGatewayException("Mercado Pago did not return the checkout link.")
            : new GatewayCheckout(id, url);
    }

    public Task<GatewayPayment> GetAsync(string accessToken, string paymentId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(paymentId)}", accessToken, null, null, cancellationToken);

    public async Task RefundAsync(string accessToken, string paymentId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var response = await ExchangeAsync(
            HttpMethod.Post,
            $"v1/payments/{Uri.EscapeDataString(paymentId)}/refunds",
            accessToken,
            new JsonObject(),
            idempotencyKey,
            cancellationToken);
    }

    private async Task<GatewayPayment> SendAsync(
        HttpMethod method,
        string path,
        string accessToken,
        JsonNode? body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var response = await ExchangeAsync(method, path, accessToken, body, idempotencyKey, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        return Parse(document.RootElement);
    }

    private async Task<HttpResponseMessage> ExchangeAsync(
        HttpMethod method,
        string path,
        string accessToken,
        JsonNode? body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (idempotencyKey is not null)
        {
            request.Headers.Add("X-Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayException("Mercado Pago did not respond.", exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Mercado Pago answered {Status} on {Path}: {Detail}", (int)response.StatusCode, method.Method, detail);
            response.Dispose();
            throw new PaymentGatewayException($"Mercado Pago answered {(int)response.StatusCode}.");
        }

        return response;
    }

    private static void AddNotificationUrl(JsonObject body, string? notificationUrl)
    {
        if (notificationUrl is not null)
        {
            body["notification_url"] = notificationUrl;
        }
    }

    private static GatewayPayment Parse(JsonElement root)
    {
        string? qrCode = null;
        string? qrCodeBase64 = null;

        if (root.TryGetProperty("point_of_interaction", out var point)
            && point.TryGetProperty("transaction_data", out var data))
        {
            qrCode = StringOf(data, "qr_code");
            qrCodeBase64 = StringOf(data, "qr_code_base64");
        }

        var expiration = StringOf(root, "date_of_expiration");

        return new GatewayPayment(
            root.GetProperty("id").ToString(),
            StringOf(root, "status") ?? "pending",
            StringOf(root, "status_detail"),
            (long)Math.Round(root.GetProperty("transaction_amount").GetDecimal() * 100, MidpointRounding.AwayFromZero),
            StringOf(root, "external_reference"),
            qrCode,
            qrCodeBase64,
            expiration is null ? null : DateTimeOffset.Parse(expiration, CultureInfo.InvariantCulture).ToUniversalTime());
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed class EvolutionWhatsAppSender(HttpClient http, IConfiguration configuration) : IWhatsAppSender
{
    public string? PlatformApiKey { get; } = configuration["Evolution:ApiKey"];

    public bool IsConfigured { get; } = !string.IsNullOrWhiteSpace(configuration["Evolution:BaseUrl"]);

    public bool CanProvision => IsConfigured && !string.IsNullOrWhiteSpace(PlatformApiKey);

    public async Task SendTextAsync(string instanceName, string apiKey, string phone, string text, CancellationToken cancellationToken)
    {
        using var response = await PostAsync($"message/sendText/{Uri.EscapeDataString(instanceName)}", apiKey, new { number = phone, text }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendImageAsync(string instanceName, string apiKey, string phone, string imageBase64, string? caption, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(
            $"message/sendMedia/{Uri.EscapeDataString(instanceName)}",
            apiKey,
            new { number = phone, mediatype = "image", mimetype = "image/png", media = imageBase64, caption, fileName = "pix.png" },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task EnsureInstanceAsync(string instanceName, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(
            "instance/create",
            PlatformApiKey!,
            new { instanceName, integration = "WHATSAPP-BAILEYS", qrcode = true },
            cancellationToken);

        // Evolution answers 403 when the instance already exists, which is the expected state on a reconnect.
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Conflict))
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task SetWebhookAsync(string instanceName, string webhookUrl, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(
            $"webhook/set/{Uri.EscapeDataString(instanceName)}",
            PlatformApiKey!,
            new { webhook = new { enabled = true, url = webhookUrl, byEvents = false, base64 = false, events = new[] { "MESSAGES_UPSERT" } } },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<(string? QrCode, string? PairingCode)> GetConnectCodesAsync(string instanceName, string? phone, CancellationToken cancellationToken)
    {
        var path = $"instance/connect/{Uri.EscapeDataString(instanceName)}";
        using var document = await GetJsonAsync(phone is null ? path : $"{path}?number={phone}", cancellationToken);
        var root = document.RootElement;

        var qr = root.TryGetProperty("base64", out var direct) && direct.ValueKind == JsonValueKind.String
            ? direct.GetString()
            : root.TryGetProperty("qrcode", out var qrcode) && qrcode.TryGetProperty("base64", out var nested) ? nested.GetString() : null;
        var pairing = root.TryGetProperty("pairingCode", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;

        return (qr, pairing);
    }

    public async Task<(bool Connected, string? Number)> GetStateAsync(string instanceName, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"instance/fetchInstances?instanceName={Uri.EscapeDataString(instanceName)}", cancellationToken);

        foreach (var instance in document.RootElement.EnumerateArray())
        {
            var connected = instance.TryGetProperty("connectionStatus", out var status) && status.GetString() == "open";
            var owner = instance.TryGetProperty("ownerJid", out var jid) && jid.ValueKind == JsonValueKind.String ? jid.GetString() : null;

            return (connected, owner?.Split('@')[0]);
        }

        return (false, null);
    }

    public async Task<(string Base64, string Mime)?> GetMediaAsync(string instanceName, string apiKey, string messageId, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(
            $"chat/getBase64FromMediaMessage/{Uri.EscapeDataString(instanceName)}",
            apiKey,
            new { message = new { key = new { id = messageId } }, convertToMp4 = false },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;

        return root.TryGetProperty("base64", out var base64) && base64.ValueKind == JsonValueKind.String
            ? (base64.GetString()!, root.TryGetProperty("mimetype", out var mime) && mime.ValueKind == JsonValueKind.String ? mime.GetString()! : "audio/ogg")
            : null;
    }

    public async Task SendAudioAsync(string instanceName, string apiKey, string phone, string audioBase64, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(
            $"message/sendWhatsAppAudio/{Uri.EscapeDataString(instanceName)}",
            apiKey,
            new { number = phone, audio = audioBase64 },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutAsync(string instanceName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"instance/logout/{Uri.EscapeDataString(instanceName)}");
        request.Headers.Add("apikey", PlatformApiKey);

        using var response = await http.SendAsync(request, cancellationToken);

        // Evolution answers 400 or 404 when the instance is already disconnected, which is the requested state.
        if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound))
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("apikey", PlatformApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private Task<HttpResponseMessage> PostAsync<T>(string path, string apiKey, T body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("apikey", apiKey);

        return http.SendAsync(request, cancellationToken);
    }
}

public sealed class PublicLookupClient(HttpClient http) : ILookupClient
{
    public async Task<CnpjData?> FindCnpjAsync(string cnpj, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"https://brasilapi.com.br/api/cnpj/v1/{cnpj}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;

        var address = string.Join(
            ", ",
            new[] { Text(root, "logradouro"), Text(root, "numero"), Text(root, "bairro"), Text(root, "municipio"), Text(root, "uf"), Text(root, "cep") }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new CnpjData(
            cnpj,
            Text(root, "razao_social") ?? string.Empty,
            Text(root, "nome_fantasia"),
            Text(root, "ddd_telefone_1"),
            Text(root, "email"),
            address.Length == 0 ? null : address,
            Text(root, "descricao_situacao_cadastral"),
            Text(root, "logradouro"),
            Text(root, "numero"),
            Text(root, "bairro"),
            Text(root, "municipio"),
            Text(root, "uf"),
            Text(root, "cep"),
            Code(root, "codigo_municipio_ibge"));
    }

    public async Task<CepData?> FindCepAsync(string cep, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"https://viacep.com.br/ws/{cep}/json/", cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("erro", out _))
        {
            return null;
        }

        return new CepData(cep, Text(root, "logradouro"), Text(root, "bairro"), Text(root, "localidade") ?? string.Empty, Text(root, "uf") ?? string.Empty);
    }

    /// <summary>BrasilAPI sends some codes as numbers (IBGE city code); accepts both.</summary>
    private static string? Code(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : Text(element, name);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
