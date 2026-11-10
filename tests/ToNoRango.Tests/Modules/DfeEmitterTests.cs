using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Microsoft.Extensions.Logging.Abstractions;
using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Orders;
using ToNoRango.Infrastructure.Fiscal;

namespace ToNoRango.Tests.Modules;

/// <summary>
/// Builds real NF-e and NFC-e with a throwaway certificate, signs them and checks them against the
/// official SEFAZ schemas. Nothing is sent: transmission needs a real certificate and homologation.
/// </summary>
public class DfeEmitterTests
{
    private const string Cnpj = "11222333000181";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EstablishmentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.FromHours(-3));

    private static (byte[] Pfx, string Password) TestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=LOJA TESTE LTDA:{Cnpj}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddYears(1));
        return (certificate.Export(X509ContentType.Pfx, "senha-teste"), "senha-teste");
    }

    private static FiscalProfile Profile()
    {
        var profile = new FiscalProfile(TenantId, EstablishmentId);
        profile.Update("LOJA TESTE LTDA", "To-No-Rango Teste", Cnpj, "0623079040081", "Rua das Flores", "100", "Centro", "Contagem", "MG", "32000000", "3118601", 1, 10);
        profile.SetRegime(TaxRegime.LucroPresumido);
        return profile;
    }

    private static (Order Order, Dictionary<Guid, ProductFiscalData> Taxes) PaidOrder(FulfillmentType fulfillment = FulfillmentType.Pickup)
    {
        var order = Order.Create(TenantId, EstablishmentId, "TR-20261110-ABCDEF12", "Ana Cliente", "31999998888", fulfillment, PaymentMethod.Cash,
            fulfillment == FulfillmentType.Delivery ? "Rua A" : null, fulfillment == FulfillmentType.Delivery ? "10" : null, null,
            fulfillment == FulfillmentType.Delivery ? "Centro" : null, fulfillment == FulfillmentType.Delivery ? "Contagem" : null,
            fulfillment == FulfillmentType.Delivery ? "MG" : null, fulfillment == FulfillmentType.Delivery ? "32000000" : null,
            null, null, fulfillment == FulfillmentType.Delivery ? 700 : 0, "teste", Now);
        var burger = new MenuItem(TenantId, EstablishmentId, Guid.NewGuid(), "X Burger", null, null, 2590, true, 0, Now);
        var soda = new MenuItem(TenantId, EstablishmentId, Guid.NewGuid(), "Refrigerante lata", null, null, 650, true, 0, Now);
        order.AddItem(burger, 2, null, null);
        order.AddItem(soda, 1, null, null, PrepStation.Bar);
        order.ApplyDiscount(300, "cupom", Now);

        var food = new ProductFiscalData(TenantId, EstablishmentId, burger.Id);
        food.Update("21069090", "5102", null, "00", 18m, "01", 0.65m, 3m);
        var drink = new ProductFiscalData(TenantId, EstablishmentId, soda.Id);
        drink.Update("22021000", "5405", "0300700", "60", 0m, "04", 0m, 0m);

        return (order, new Dictionary<Guid, ProductFiscalData> { [burger.Id] = food, [soda.Id] = drink });
    }

    private static DfeIssue Issue(int model, NfeRecipientRequest? recipient = null, FulfillmentType fulfillment = FulfillmentType.Pickup)
    {
        var (pfx, password) = TestCertificate();
        var profile = Profile();
        var (order, taxes) = PaidOrder(fulfillment);
        var document = new FiscalDocument(TenantId, EstablishmentId, order.Id, order.Code, 1, 10, 2, order.TotalInCents, Now, model, recipient?.Document, recipient?.Name);
        return DfeService.BuildIssue(profile, document, order, taxes, recipient, Now, new DfeCredentials(pfx, password, "1", "0123456789ABCDEF0123456789ABCDEF"));
    }

    private static ZeusDfeEmitter Emitter() => new(NullLogger<ZeusDfeEmitter>.Instance);

    private static void AssertSignatureValid(string xml)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);
        var signature = (XmlElement)document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
        var signed = new SignedXml(document);
        signed.LoadXml(signature);
        Assert.True(signed.CheckSignature(), "A assinatura digital não confere.");
    }

    [Fact]
    public void Nfce_AssinadaEValidaNoEsquemaOficial()
    {
        var result = Emitter().Sign(Issue(65));

        Assert.Equal(DfeOutcome.SignedOffline, result.Outcome);
        Assert.Equal(44, result.AccessKey!.Length);
        Assert.Equal("65", result.AccessKey.Substring(20, 2));
        Assert.Equal(Cnpj, result.AccessKey.Substring(6, 14));
        Assert.Contains("<qrCode>", result.Xml);
        Assert.Contains("<vNF>55.30</vNF>", result.Xml); // 2 × 25,90 + 6,50 − 3,00
        Assert.Contains("NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO", result.Xml);
        AssertSignatureValid(result.Xml!);
    }

    [Fact]
    public void Nfe_ComDestinatarioEFreteAssinadaEValida()
    {
        var recipient = new NfeRecipientRequest("52998224725", "Ana Cliente", null, "Rua A", "10", "Centro", "3118601", "Contagem", "MG", "32000000", "ana@exemplo.com");
        var result = Emitter().Sign(Issue(55, recipient, FulfillmentType.Delivery));

        Assert.Equal("55", result.AccessKey!.Substring(20, 2));
        Assert.Contains("<CPF>52998224725</CPF>", result.Xml);
        Assert.Contains("<vFrete>7.00</vFrete>", result.Xml);
        Assert.Contains("<vNF>62.30</vNF>", result.Xml);
        Assert.DoesNotContain("<qrCode>", result.Xml);
        AssertSignatureValid(result.Xml!);
    }

    [Fact]
    public void ContingenciaOffline_TemOutraChaveEJustificativa()
    {
        var issue = Issue(65);
        var normal = Emitter().Sign(issue);
        var offline = Emitter().Sign(issue with { OfflineContingency = true, ContingencyAt = Now, ContingencyReason = "SEFAZ SEM RESPOSTA NO MOMENTO DA VENDA" });

        Assert.Equal("9", offline.AccessKey!.Substring(34, 1));
        Assert.NotEqual(normal.AccessKey, offline.AccessKey);
        Assert.Contains("<tpEmis>9</tpEmis>", offline.Xml);
        Assert.Contains("<xJust>SEFAZ SEM RESPOSTA NO MOMENTO DA VENDA</xJust>", offline.Xml);
        AssertSignatureValid(offline.Xml!);
    }

    [Fact]
    public void Nfce_SemCscNaoEmite()
    {
        var issue = Issue(65);
        Assert.Throws<InvalidOperationException>(() => Emitter().Sign(issue with { Credentials = issue.Credentials with { CscId = null } }));
    }

    [Fact]
    public void Certificado_LeCnpjEValidade()
    {
        var (pfx, password) = TestCertificate();
        var read = Emitter().ReadCertificate(pfx, password);

        Assert.Equal(Cnpj, read.Cnpj);
        Assert.True(read.ExpiresAt > Now);
        Assert.ThrowsAny<CryptographicException>(() => Emitter().ReadCertificate(pfx, "errada"));
    }

    [Fact]
    public void Rateio_DeFreteEDescontoFechaNoCentavo()
    {
        var parts = DfeService.Spread(1000, [2590 * 2, 650]);
        Assert.Equal(1000, parts.Sum());
        Assert.All(parts, part => Assert.True(part >= 0));
    }

    [Fact]
    public void Inutilizacao_ExigeJustificativaEFaixaValida()
    {
        Assert.Throws<ArgumentException>(() => new FiscalNumberGap(TenantId, EstablishmentId, 65, 1, 5, 4, 2, "falha no sistema de vendas", Now));
        Assert.Throws<ArgumentException>(() => new FiscalNumberGap(TenantId, EstablishmentId, 65, 1, 5, 6, 2, "curta", Now));
        var gap = new FiscalNumberGap(TenantId, EstablishmentId, 65, 1, 5, 6, 2, "numeracao pulada por falha no sistema", Now);
        Assert.Equal(NumberGapStatus.Pending, gap.Status);
    }

    [Fact]
    public void Cancelamento_SoDeNotaAutorizada()
    {
        var document = new FiscalDocument(TenantId, EstablishmentId, Guid.NewGuid(), "X", 1, 1, 2, 100, Now);
        Assert.Throws<InvalidOperationException>(() => document.Cancel("135", "cliente desistiu da compra", null, Now));

        document.Authorize("1".PadLeft(44, '1'), "123", "<xml/>", "ok", Now);
        document.Cancel("135000000000001", "cliente desistiu da compra", "<evento/>", Now);
        Assert.Equal(FiscalDocumentStatus.Cancelled, document.Status);
    }
}
