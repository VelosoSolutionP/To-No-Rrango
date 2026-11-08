using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using ToNoRango.Application.Abstractions;

namespace ToNoRango.Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');

        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>AES-GCM for the credentials establishments give us. The stored value is nonce + tag + ciphertext.</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        var configured = configuration["Security:EncryptionKey"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("Security:EncryptionKey is required (32 bytes in base64).");
        }

        _key = Convert.FromBase64String(configured);

        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Security:EncryptionKey must decode to exactly 32 bytes.");
        }
    }

    public string Protect(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));

        return Convert.ToBase64String(output);
    }

    public string Unprotect(string cipherText)
    {
        var input = Convert.FromBase64String(cipherText);
        var plain = new byte[input.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plain);

        return Encoding.UTF8.GetString(plain);
    }
}
