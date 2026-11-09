using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using ToNoRango.Application.Fiscal;

namespace ToNoRango.Infrastructure.Integrations;

/// <summary>
/// NFC-e through ACBrMonitorPLUS over TCP. The monitor owns the A1 certificate, the CSC and the SEFAZ
/// connection. A command ends with CR LF "." CR LF; an answer ends with ETX and starts with "OK:" or "ERRO:".
/// </summary>
public sealed class AcbrFiscalGateway(IConfiguration configuration) : IFiscalGateway
{
    private const char EndOfText = '\u0003';
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private string Host => configuration["Acbr:Host"] ?? "host.docker.internal";
    private int Port => int.TryParse(configuration["Acbr:Port"], out var port) ? port : 3434;
    private int TimeoutSeconds => int.TryParse(configuration["Acbr:TimeoutSeconds"], out var seconds) ? seconds : 120;

    public bool IsConfigured => string.Equals(configuration["Acbr:Mode"], "Monitor", StringComparison.OrdinalIgnoreCase);

    /// <summary>Homologation unless production is set on purpose.</summary>
    public int Environment => configuration["Acbr:Environment"] == "1" ? 1 : NfceIniBuilder.HomologationEnvironment;

    public async Task<FiscalIssueResult> IssueAsync(string ini, CancellationToken cancellationToken)
    {
        string answer;

        try
        {
            answer = await ExecuteAsync($"NFe.CriarEnviarNFe({Quote(ini)},1,0,\"\",0,0,\"\",0)", cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return new FiscalIssueResult(
                FiscalIssueOutcome.Unanswered,
                $"Sem resposta do emissor fiscal em {Host}:{Port}. Tente de novo: a nota usa o mesmo número.");
        }

        return ReadAnswer(answer);
    }

    public static FiscalIssueResult ReadAnswer(string answer)
    {
        var text = answer.Trim();

        if (text.StartsWith("ERRO", StringComparison.OrdinalIgnoreCase))
        {
            return new FiscalIssueResult(FiscalIssueOutcome.Rejected, text.Length > 5 ? text[5..].Trim() : text);
        }

        if (text.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
        {
            text = text[3..].Trim();
        }

        var sections = ParseIni(text);
        var values = sections
            .Where(section => section.Key.StartsWith("NFe", StringComparison.OrdinalIgnoreCase) && section.Value.ContainsKey("chNFe"))
            .Select(section => section.Value)
            .FirstOrDefault()
            ?? sections.Where(section => section.Key.Equals("Retorno", StringComparison.OrdinalIgnoreCase)).Select(section => section.Value).FirstOrDefault()
            ?? sections[string.Empty];

        values.TryGetValue("cStat", out var status);
        var message = values.TryGetValue("xMotivo", out var reason) ? reason : text;

        if (status is not ("100" or "150"))
        {
            return new FiscalIssueResult(FiscalIssueOutcome.Rejected, status is null ? message : $"{status}: {message}");
        }

        values.TryGetValue("chNFe", out var accessKey);
        values.TryGetValue("nProt", out var protocol);
        values.TryGetValue("XML", out var xml);
        return new FiscalIssueResult(FiscalIssueOutcome.Authorized, message, accessKey, protocol, xml);
    }

    public static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        using var client = new TcpClient();
        await client.ConnectAsync(Host, Port, timeout.Token);
        await using var stream = client.GetStream();

        // O monitor cumprimenta ao conectar e atende um comando por conexão.
        await ReadUntilEndAsync(stream, timeout.Token);
        await stream.WriteAsync(Latin1.GetBytes(command + "\r\n.\r\n"), timeout.Token);
        return await ReadUntilEndAsync(stream, timeout.Token);
    }

    private static async Task<string> ReadUntilEndAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new byte[8192];

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                throw new IOException("Conexão encerrada pelo emissor fiscal.");
            }

            var chunk = Latin1.GetString(buffer, 0, read);
            var end = chunk.IndexOf(EndOfText);

            if (end >= 0)
            {
                return text.Append(chunk, 0, end).ToString();
            }

            text.Append(chunk);
        }
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = new(StringComparer.OrdinalIgnoreCase)
        };
        var current = sections[string.Empty];

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[line[1..^1]] = current;
                continue;
            }

            var separator = line.IndexOf('=');

            if (separator > 0)
            {
                current[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return sections;
    }
}
