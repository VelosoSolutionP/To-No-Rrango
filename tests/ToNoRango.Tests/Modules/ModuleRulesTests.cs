using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToNoRango.Application.Fiscal;
using ToNoRango.Application.Messaging;
using ToNoRango.Application.Payments;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Contracts;
using ToNoRango.Domain.Delivery;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Identity;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Payments;
using ToNoRango.Infrastructure.Security;

namespace ToNoRango.Tests.Modules;

public class ModuleRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Establishment = Guid.NewGuid();

    [Theory]
    [InlineData("(11) 98888-7777", "1188887777")]
    [InlineData("5511988887777", "1188887777")]
    [InlineData("551188887777", "1188887777")]
    public void TelefoneComOuSemNonoDigito_TemAMesmaChave(string phone, string key) =>
        Assert.Equal(key, PhoneNumber.Key(phone));

    [Fact]
    public void TelefoneNacional_RecebeDdiParaWhatsApp() =>
        Assert.Equal("5511988887777", PhoneNumber.ToWhatsApp("(11) 98888-7777"));

    [Theory]
    [InlineData("52998224725", true)]
    [InlineData("11111111111", false)]
    [InlineData("52998224724", false)]
    [InlineData("11222333000181", true)]
    [InlineData("11222333000182", false)]
    [InlineData("123", false)]
    public void Documento_ValidaDigitosVerificadores(string document, bool valid) =>
        Assert.Equal(valid, DocumentValidator.IsValid(document));

    [Fact]
    public void Compra_RecalculaCustoMedioPonderado()
    {
        var item = new StockItem(Tenant, Establishment, "Pão", "un", 0, Now);

        item.ReceivePurchase(10, 100, Now);
        item.ReceivePurchase(10, 200, Now);

        Assert.Equal(20, item.QuantityOnHand);
        Assert.Equal(150, item.AverageCostInCents);
    }

    [Fact]
    public void CompraComSaldoNegativo_NaoDistorceCustoMedio()
    {
        var item = new StockItem(Tenant, Establishment, "Pão", "un", 0, Now);
        item.Adjust(-5, Now);

        item.ReceivePurchase(10, 200, Now);

        Assert.Equal(5, item.QuantityOnHand);
        Assert.Equal(200, item.AverageCostInCents);
    }

    [Fact]
    public void EstoqueNoMinimo_FicaBaixo()
    {
        var item = new StockItem(Tenant, Establishment, "Pão", "un", 5, Now);
        item.ReceivePurchase(5, 100, Now);

        Assert.True(item.IsLow);
    }

    [Fact]
    public void LancamentoBaixado_NaoPodeSerBaixadoDeNovo()
    {
        var entry = new FinancialEntry(Tenant, Establishment, FinancialType.Payable, FinancialSource.Manual, null, "Aluguel", "Novembro", 1000, new DateOnly(2026, 11, 10), Now);
        entry.Settle(Now);

        Assert.Throws<InvalidOperationException>(() => entry.Settle(Now));
    }

    [Fact]
    public void Bairro_IgnoraAcentoEMaiusculas() =>
        Assert.Equal(DeliveryZone.KeyOf("Vila São João"), DeliveryZone.KeyOf("vila sao joao"));

    [Fact]
    public void Usuario_BloqueiaAposCincoFalhas()
    {
        var user = new User(Tenant, "Ana", "ANA@x.dev", "hash", UserRole.Owner, Now);

        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            Assert.False(user.IsLocked(Now));
            user.RegisterFailedLogin(Now);
        }

        Assert.True(user.IsLocked(Now));
        Assert.False(user.IsLocked(Now.Add(User.LockoutDuration).AddSeconds(1)));
        Assert.Equal("ana@x.dev", user.Email);
    }

    [Fact]
    public void ContratoRepublicado_AtualizaRemoveEAdicionaModulos()
    {
        var contract = new Contract(Tenant, Guid.NewGuid(), Establishment, new DateOnly(2026, 1, 1), null, ContractStatus.Vigente, [new ContractModule("estoque"), new ContractModule("fiscal")]);

        contract.ApplyPortalState(contract.CustomerId, Establishment, contract.StartsOn, null, ContractStatus.Vigente, [new ContractModule("estoque", false), new ContractModule("ia")], 3, null);

        Assert.Equal(["ia"], contract.LiberadosModules());
        Assert.Equal(3, contract.MaxUsers);
    }

    [Fact]
    public void Mensagem_ParaDeTentarAposLimite()
    {
        var message = new OutboundMessage(Tenant, Establishment, "11988887777", "oi", Now);
        Assert.Equal("5511988887777", message.Phone);

        message.MarkFailed("erro", Now);
        Assert.Equal(OutboundStatus.Pending, message.Status);
        Assert.True(message.NextAttemptAt >= Now);
    }

    [Theory]
    [InlineData("approved", ChargeStatus.Approved)]
    [InlineData("charged_back", ChargeStatus.Refunded)]
    [InlineData("in_process", ChargeStatus.Pending)]
    public void StatusDoGateway_EMapeado(string status, ChargeStatus expected) =>
        Assert.Equal(expected, PaymentService.MapStatus(status));

    [Fact]
    public void AssinaturaMercadoPago_AceitaValidaERecusaAlterada()
    {
        var hash = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("segredo"),
            Encoding.UTF8.GetBytes("id:abc123;request-id:req-1;ts:1700;")));

        Assert.True(MercadoPagoSignature.IsValid("segredo", $"ts=1700,v1={hash}", "req-1", "ABC123"));
        Assert.False(MercadoPagoSignature.IsValid("segredo", $"ts=1701,v1={hash}", "req-1", "ABC123"));
        Assert.False(MercadoPagoSignature.IsValid("outro", $"ts=1700,v1={hash}", "req-1", "ABC123"));
        Assert.False(MercadoPagoSignature.IsValid("segredo", null, "req-1", "ABC123"));
    }

    [Fact]
    public void Senha_NaoEGuardadaEmTextoEVerifica()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("senha-forte");

        Assert.DoesNotContain("senha-forte", hash);
        Assert.True(hasher.Verify("senha-forte", hash));
        Assert.False(hasher.Verify("errada", hash));
    }

    private static JsonElement Evolution(string jid, bool fromMe, long timestamp, string message) =>
        JsonDocument.Parse($$$"""{"event":"messages.upsert","data":{"key":{"remoteJid":"{{{jid}}}","fromMe":{{{(fromMe ? "true" : "false")}}},"id":"M1"},"pushName":"Ana","messageTimestamp":{{{timestamp}}},"message":{{{message}}}}}""").RootElement;

    [Fact]
    public void EvolutionTexto_ViraMensagem()
    {
        var parsed = EvolutionWebhookParser.Parse(Evolution("5511988887777@s.whatsapp.net", false, Now.ToUnixTimeSeconds(), """{"conversation":"oi"}"""), Now);

        Assert.Equal("oi", parsed!.Text);
        Assert.Equal("5511988887777", parsed.Phone);
    }

    [Fact]
    public void EvolutionAudio_EReconhecido() =>
        Assert.True(EvolutionWebhookParser.Parse(Evolution("5511988887777@s.whatsapp.net", false, Now.ToUnixTimeSeconds(), """{"audioMessage":{}}"""), Now)!.IsAudio);

    [Theory]
    [InlineData("123@g.us", false, 0)]
    [InlineData("5511988887777@s.whatsapp.net", true, 0)]
    [InlineData("5511988887777@s.whatsapp.net", false, 3600)]
    public void EvolutionGrupoEcoOuAntiga_EIgnorada(string jid, bool fromMe, int ageSeconds) =>
        Assert.Null(EvolutionWebhookParser.Parse(Evolution(jid, fromMe, Now.ToUnixTimeSeconds() - ageSeconds, """{"conversation":"oi"}"""), Now));

    [Fact]
    public void EvolutionLid_UsaNumeroReal()
    {
        var json = "{\"event\":\"messages.upsert\",\"data\":{\"key\":{\"remoteJid\":\"123@lid\",\"remoteJidAlt\":\"5511988887777@s.whatsapp.net\",\"fromMe\":false,\"id\":\"M2\"},\"messageTimestamp\":" + Now.ToUnixTimeSeconds() + ",\"message\":{\"conversation\":\"oi\"}}}";
        var payload = JsonDocument.Parse(json).RootElement;

        Assert.Equal("5511988887777", EvolutionWebhookParser.Parse(payload, Now)!.Phone);
    }

    [Fact]
    public void EvolutionMensagemTemporaria_ELida()
    {
        var parsed = EvolutionWebhookParser.Parse(Evolution("5511988887777@s.whatsapp.net", false, Now.ToUnixTimeSeconds(), """{"ephemeralMessage":{"message":{"extendedTextMessage":{"text":"quero pedir"}}}}"""), Now);

        Assert.Equal("quero pedir", parsed!.Text);
    }

    private const string Nfe = """
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe><infNFe Id="NFe35201111222333000181550010000000011000000011">
        <ide><nNF>77</nNF><dhEmi>2026-11-01T10:00:00-03:00</dhEmi></ide>
        <emit><CNPJ>11222333000181</CNPJ><xNome>Fornece LTDA</xNome><xFant>Fornece</xFant></emit>
        <dest><CNPJ>00000000000191</CNPJ></dest>
        <det nItem="1"><prod><cProd>A1</cProd><xProd>Farinha</xProd><NCM>11010010</NCM><CFOP>5102</CFOP><uCom>KG</uCom><qCom>2.5000</qCom><vUnCom>4.2000</vUnCom><vProd>10.50</vProd></prod></det>
        <total><ICMSTot><vNF>10.50</vNF></ICMSTot></total>
        </infNFe></NFe></nfeProc>
        """;

    [Fact]
    public void Nfe_ELidaComSeparadorDecimalInvariante()
    {
        var parsed = NfeXmlParser.Parse(Nfe);

        Assert.Equal(44, parsed.AccessKey.Length);
        Assert.Equal("77", parsed.Number);
        Assert.Equal(new DateOnly(2026, 11, 1), parsed.IssuedOn);
        Assert.Equal(2.5m, parsed.Items[0].Quantity);
        Assert.Equal(10.5m, parsed.Total);
    }

    [Theory]
    [InlineData("<!DOCTYPE a [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><a>&x;</a>")]
    [InlineData("nao e xml")]
    [InlineData("<a/>")]
    public void Nfe_RecusaArquivoInvalidoOuComEntidadeExterna(string xml) =>
        Assert.Throws<ArgumentException>(() => NfeXmlParser.Parse(xml));
}
