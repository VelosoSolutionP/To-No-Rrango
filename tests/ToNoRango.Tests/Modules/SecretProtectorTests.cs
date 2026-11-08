using Microsoft.Extensions.Configuration;
using ToNoRango.Infrastructure.Security;

namespace ToNoRango.Tests.Modules;

public class SecretProtectorTests
{
    private static AesGcmSecretProtector Protector(string? key) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:EncryptionKey"] = key }).Build());

    private static string NewKey() => Convert.ToBase64String(new byte[32].Select((_, index) => (byte)index).ToArray());

    [Fact]
    public void Segredo_ERecuperadoENaoFicaLegivel()
    {
        var protector = Protector(NewKey());
        var cipher = protector.Protect("APP_USR-token");

        Assert.DoesNotContain("APP_USR", cipher);
        Assert.NotEqual(cipher, protector.Protect("APP_USR-token"));
        Assert.Equal("APP_USR-token", protector.Unprotect(cipher));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AAAA")]
    public void ChaveAusenteOuCurta_FalhaNaInicializacao(string? key) =>
        Assert.Throws<InvalidOperationException>(() => Protector(key));
}
