using System.Security.Cryptography;
using System.Text;

namespace ToNoRango.Application.Payments;

/// <summary>Validates the x-signature header Mercado Pago sends with each webhook notification.</summary>
public static class MercadoPagoSignature
{
    public static bool IsValid(string secret, string? signatureHeader, string? requestId, string? dataId)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(dataId))
        {
            return false;
        }

        string? timestamp = null;
        string? hash = null;

        foreach (var part in signatureHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);

            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0] == "ts")
            {
                timestamp = pair[1];
            }
            else if (pair[0] == "v1")
            {
                hash = pair[1];
            }
        }

        if (timestamp is null || hash is null)
        {
            return false;
        }

        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{timestamp};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));
        var received = Encoding.UTF8.GetBytes(hash.ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Convert.ToHexStringLower(expected)),
            received);
    }
}
