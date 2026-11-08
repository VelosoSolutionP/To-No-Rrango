namespace ToNoRango.Domain.Common;

/// <summary>Check-digit validation for CPF and CNPJ. It proves the number is well formed, not that it exists.</summary>
public static class DocumentValidator
{
    public static bool IsValid(string? document)
    {
        var digits = PhoneNumber.Digits(document);
        return digits.Length switch
        {
            11 => IsValidCpf(digits),
            14 => IsValidCnpj(digits),
            _ => false
        };
    }

    public static bool IsValidCpf(string? cpf)
    {
        var digits = PhoneNumber.Digits(cpf);

        if (digits.Length != 11 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        return CheckDigit(digits[..9], [10, 9, 8, 7, 6, 5, 4, 3, 2]) == digits[9] - '0'
            && CheckDigit(digits[..10], [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]) == digits[10] - '0';
    }

    public static bool IsValidCnpj(string? cnpj)
    {
        var digits = PhoneNumber.Digits(cnpj);

        if (digits.Length != 14 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        return CheckDigit(digits[..12], [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == digits[12] - '0'
            && CheckDigit(digits[..13], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == digits[13] - '0';
    }

    private static int CheckDigit(string digits, int[] weights)
    {
        var remainder = digits.Select((digit, index) => (digit - '0') * weights[index]).Sum() % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
