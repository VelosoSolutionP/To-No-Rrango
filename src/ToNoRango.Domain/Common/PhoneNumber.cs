namespace ToNoRango.Domain.Common;

/// <summary>
/// Brazilian phone handling. WhatsApp JIDs may omit the ninth digit, so identity is compared by
/// <see cref="Key"/> (area code + last eight digits), while messages are sent to the full number.
/// </summary>
public static class PhoneNumber
{
    public static string Digits(string? raw) => new((raw ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string ToWhatsApp(string? raw)
    {
        var digits = Digits(raw);
        return digits.Length is 10 or 11 ? $"55{digits}" : digits;
    }

    public static string Key(string? raw)
    {
        var digits = Digits(raw);

        if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        return digits.Length < 10 ? digits : $"{digits[..2]}{digits[^8..]}";
    }
}
