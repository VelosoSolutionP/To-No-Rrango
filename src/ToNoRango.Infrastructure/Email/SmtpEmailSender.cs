using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using ToNoRango.Application.Abstractions;

namespace ToNoRango.Infrastructure.Email;

/// <summary>
/// Outgoing e-mail over SMTP (the same Zoho account as the rest of Veloso Solution). Configured by
/// environment only: Email__Host, Email__Port (465 = TLS from the start), Email__User, Email__Password,
/// Email__From (defaults to the user) and Email__Name. Without user and password it stays off.
/// </summary>
public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    private string? User => Value("Email:User");

    // Compose passes unset variables as empty strings: empty means "use the default".
    private string? Value(string key) => string.IsNullOrWhiteSpace(configuration[key]) ? null : configuration[key]!.Trim();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(configuration["Email:Password"]);

    public async Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return "Envio de e-mail não configurado no servidor.";
        }

        if (!MailboxAddress.TryParse(message.To, out var to))
        {
            return $"E-mail inválido: {message.To}";
        }

        var host = Value("Email:Host") ?? "smtp.zoho.com";
        var port = int.TryParse(configuration["Email:Port"], out var configured) ? configured : 465;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(Value("Email:Name") ?? "To No Rango", Value("Email:From") ?? User!));
        mime.To.Add(to);
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 20_000 };
        await client.ConnectAsync(host, port, port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(User!, configuration["Email:Password"]!, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return null;
    }
}
