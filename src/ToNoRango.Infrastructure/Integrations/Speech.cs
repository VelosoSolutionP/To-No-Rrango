using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ToNoRango.Application.Abstractions;

namespace ToNoRango.Infrastructure.Integrations;

/// <summary>
/// Sends the audio to a whisper.cpp server (POST /inference), the same local server ZEST uses.
/// Configured by Transcription:Url; without it, audio keeps getting the "send it as text" answer.
/// </summary>
public sealed partial class WhisperSpeechToText(HttpClient http, IConfiguration configuration, ILogger<WhisperSpeechToText> logger) : ISpeechToText
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly string? _url = configuration["Transcription:Url"]?.TrimEnd('/');

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_url);

    public async Task<string?> TranscribeAsync(byte[] audio, string mime, CancellationToken cancellationToken)
    {
        if (!IsConfigured || audio.Length == 0 || audio.Length > MaxBytes)
        {
            return null;
        }

        try
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(audio);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(mime) ? "audio/ogg" : mime.Split(';')[0]);
            form.Add(file, "file", "audio.ogg");
            form.Add(new StringContent("json"), "response_format");
            form.Add(new StringContent("pt"), "language");

            using var response = await http.PostAsync($"{_url}/inference", form, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Transcription server answered {Status}", (int)response.StatusCode);
                return null;
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return Clean(document.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not transcribe audio");
            return null;
        }
    }

    /// <summary>Whisper writes [Música] or [BLANK_AUDIO] when nobody speaks; that is silence, not the customer.</summary>
    public static string? Clean(string? text)
    {
        var cleaned = Whitespace().Replace(Blank().Replace(text ?? string.Empty, " "), " ").Trim();
        return cleaned.Length < 2 || Noise().IsMatch(cleaned) ? null : cleaned;
    }

    [GeneratedRegex(@"\[BLANK_AUDIO\]", RegexOptions.IgnoreCase)]
    private static partial Regex Blank();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s*[\[(][^\])]*[\])]\s*$")]
    private static partial Regex Noise();
}

/// <summary>
/// Local voice with piper (pt_BR) and ffmpeg to make a WhatsApp voice note. Configured by
/// Voice:PiperPath and Voice:Model; without them, replies to audio go only as text.
/// </summary>
public sealed partial class PiperTextToSpeech(IConfiguration configuration, ILogger<PiperTextToSpeech> logger) : ITextToSpeech
{
    private const int MaxChars = 600;
    private readonly string? _piper = configuration["Voice:PiperPath"];
    private readonly string? _model = configuration["Voice:Model"];
    private readonly string _ffmpeg = configuration["Voice:Ffmpeg"] ?? "ffmpeg";

    public bool IsConfigured => File.Exists(_piper) && File.Exists(_model);

    public async Task<string?> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        var spoken = ForSpeech(text);

        if (!IsConfigured || spoken.Length == 0)
        {
            return null;
        }

        var folder = Path.Combine(Path.GetTempPath(), "tnr-voz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var wav = Path.Combine(folder, "voz.wav");
        var ogg = Path.Combine(folder, "voz.ogg");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            if (!await RunAsync(_piper!, ["--model", _model!, "--output_file", wav], spoken, timeout.Token)
                || !await RunAsync(_ffmpeg, ["-y", "-loglevel", "error", "-i", wav, "-c:a", "libopus", "-b:a", "32k", "-ac", "1", ogg], null, timeout.Token)
                || !File.Exists(ogg))
            {
                return null;
            }

            return Convert.ToBase64String(await File.ReadAllBytesAsync(ogg, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or System.ComponentModel.Win32Exception && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not synthesize voice");
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// What is read aloud: no WhatsApp formatting, no links (they stay in the text message), and
    /// long answers cut at a sentence so the voice note stays short.
    /// </summary>
    public static string ForSpeech(string text)
    {
        var plain = Links().Replace(text ?? string.Empty, "o link que mandei por escrito");
        plain = plain.Replace("*", string.Empty).Replace("_", string.Empty).Replace("~", string.Empty);
        plain = Numbered().Replace(plain, "opção $1: ");
        plain = Whitespace().Replace(plain.Replace('\n', ' '), " ").Trim();

        if (plain.Length <= MaxChars)
        {
            return plain;
        }

        var cut = plain.LastIndexOf(". ", MaxChars, StringComparison.Ordinal);
        return (cut > 80 ? plain[..(cut + 1)] : plain[..MaxChars]) + " O restante mandei por escrito.";
    }

    private static async Task<bool> RunAsync(string file, IReadOnlyList<string> arguments, string? input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException($"Could not start {file}");

        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var drainOut = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var drainErr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await Task.WhenAll(drainOut, drainErr);
        return process.ExitCode == 0;
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Links();

    [GeneratedRegex(@"(?m)^\s*(\d+)\s+")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
