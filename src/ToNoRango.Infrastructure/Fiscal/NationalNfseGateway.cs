using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using ToNoRango.Application.Fiscal;

namespace ToNoRango.Infrastructure.Fiscal;

public sealed class NfseOptions
{
    public const string SectionName = "Nfse";

    public string ProductionUrl { get; init; } = "https://sefin.nfse.gov.br/SefinNacional";

    /// <summary>"Produção restrita": the national homologation environment.</summary>
    public string RestrictedUrl { get; init; } = "https://sefin.producaorestrita.nfse.gov.br/SefinNacional";

    public int TimeoutSeconds { get; init; } = 40;
}

/// <summary>
/// NFS-e of the national standard. The DPS is signed (XMLDSig, enveloped, RSA-SHA256) with the
/// store's A1, compressed with GZip, sent as base64 in JSON, over TLS that presents the same certificate.
/// </summary>
public sealed class NationalNfseGateway(NfseOptions options, ILogger<NationalNfseGateway> logger) : INfseGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<NfseResult> IssueAsync(int environment, string dpsXml, DfeCredentials credentials, CancellationToken cancellationToken)
    {
        using var certificate = Load(credentials);
        var signed = Sign(dpsXml, "infDPS", certificate);

        try
        {
            using var http = Client(certificate);
            using var response = await http.PostAsJsonAsync($"{Base(environment)}/nfse", new { dpsXmlGZipB64 = Pack(signed) }, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode && TryRead(body, out var answer) && answer.TryGetProperty("chaveAcesso", out var key))
            {
                var xml = answer.TryGetProperty("nfseXmlGZipB64", out var packed) && packed.GetString() is { } value ? Unpack(value) : null;
                return new NfseResult(DfeOutcome.Authorized, Alerts(answer) ?? "NFS-e autorizada.", key.GetString(), xml);
            }

            // 4xx: the government read the DPS and refused it; 5xx: unknown outcome, try again later.
            return (int)response.StatusCode is >= 400 and < 500
                ? new NfseResult(DfeOutcome.Rejected, Errors(body) ?? $"Recusada ({(int)response.StatusCode}).")
                : new NfseResult(DfeOutcome.Unanswered, $"Sefin Nacional respondeu {(int)response.StatusCode}. Tente de novo em alguns minutos.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "NFS-e issue call failed");
            return new NfseResult(DfeOutcome.Unanswered, "Sem resposta do Sefin Nacional. Tente de novo: o número da DPS é o mesmo.");
        }
    }

    public async Task<DfeEventResult> CancelAsync(int environment, string accessKey, string eventXml, DfeCredentials credentials, CancellationToken cancellationToken)
    {
        using var certificate = Load(credentials);
        var signed = Sign(eventXml, "infPedReg", certificate);

        try
        {
            using var http = Client(certificate);
            using var response = await http.PostAsJsonAsync($"{Base(environment)}/nfse/{accessKey}/eventos", new { pedidoRegistroEventoXmlGZipB64 = Pack(signed) }, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var xml = TryRead(body, out var answer) && answer.TryGetProperty("eventoXmlGZipB64", out var packed) && packed.GetString() is { } value ? Unpack(value) : null;
                return new DfeEventResult(true, "Cancelamento registrado.", null, xml);
            }

            return new DfeEventResult(false, Errors(body) ?? $"Recusado ({(int)response.StatusCode}).");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "NFS-e cancel call failed");
            return new DfeEventResult(false, "Sem resposta do Sefin Nacional. Tente de novo.");
        }
    }

    /// <summary>Enveloped XMLDSig over the element with the Id; the signature goes right after it, inside the root.</summary>
    public static string Sign(string xml, string elementName, X509Certificate2 certificate)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);
        var target = document.GetElementsByTagName(elementName, NfseXml.Namespace).OfType<XmlElement>().Single();
        var id = target.GetAttribute("Id");

        var signed = new SignedXml(document) { SigningKey = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("O certificado não tem chave RSA.") };
        signed.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signed.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference($"#{id}") { DigestMethod = SignedXml.XmlDsigSHA256Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signed.AddReference(reference);

        var info = new KeyInfo();
        info.AddClause(new KeyInfoX509Data(certificate));
        signed.KeyInfo = info;
        signed.ComputeSignature();

        document.DocumentElement!.AppendChild(document.ImportNode(signed.GetXml(), true));
        return document.OuterXml;
    }

    public static string Pack(string xml)
    {
        using var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(xml));
        }

        return Convert.ToBase64String(buffer.ToArray());
    }

    public static string Unpack(string base64)
    {
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Error list of the national API: [{ codigo, descricao, complemento }], names in any case.</summary>
    public static string? Errors(string body)
    {
        if (!TryRead(body, out var root))
        {
            return string.IsNullOrWhiteSpace(body) ? null : body.Length <= 300 ? body : body[..300];
        }

        var list = Property(root, "erros") ?? Property(root, "erro");

        if (list is not { ValueKind: JsonValueKind.Array } errors)
        {
            return Property(root, "mensagem")?.GetString() ?? Property(root, "message")?.GetString();
        }

        var lines = errors.EnumerateArray()
            .Select(error => string.Join(" - ", new[] { Property(error, "codigo")?.ToString(), Property(error, "descricao")?.GetString(), Property(error, "complemento")?.GetString() }
                .Where(part => !string.IsNullOrWhiteSpace(part))))
            .Where(line => line.Length > 0)
            .ToList();

        return lines.Count == 0 ? null : string.Join(" | ", lines);
    }

    private static string? Alerts(JsonElement root) =>
        Property(root, "alertas") is { ValueKind: JsonValueKind.Array } alerts && alerts.GetArrayLength() > 0
            ? "Autorizada com alertas: " + string.Join(" | ", alerts.EnumerateArray().Select(alert => Property(alert, "descricao")?.GetString()).OfType<string>())
            : null;

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static bool TryRead(string body, out JsonElement root)
    {
        try
        {
            root = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body).RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            root = default;
            return false;
        }
    }

    private string Base(int environment) => (environment == 1 ? options.ProductionUrl : options.RestrictedUrl).TrimEnd('/');

    private HttpClient Client(X509Certificate2 certificate)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            SslOptions = { ClientCertificates = new X509CertificateCollection { certificate } },
        };

        var client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZEST-Fiscal/1.0");
        return client;
    }

    private static X509Certificate2 Load(DfeCredentials credentials) =>
        X509CertificateLoader.LoadPkcs12(credentials.Pfx, credentials.Password, X509KeyStorageFlags.EphemeralKeySet);
}
