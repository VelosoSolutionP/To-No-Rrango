using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using NSubstitute;
using Shouldly;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Infrastructure.Fiscal;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Tests.Modules;

public class NfseTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();
    private static readonly XNamespace Ns = NfseXml.Namespace;

    private static ServiceNoteRequest Request(string externalId = "os-1", decimal? rate = 3m, bool withheld = false) =>
        new(externalId, new ServiceTaker("11444777000161", "Cliente SA", "fin@cliente.com", "Rua B", "2", "Centro", "3118601", "32000-000"),
            "010101", "Desenvolvimento de sistema sob encomenda", 150000, rate, withheld);

    private static FiscalProfile Profile(bool certificate = true)
    {
        var profile = new FiscalProfile(Tenant, Store);
        profile.Update("LOJA TESTE LTDA", "Loja", "11444777000161", "0623079040081", "Rua A", "1", "Centro", "Contagem", "MG", "32000000", "3118601", 1, 1);
        profile.SetServiceSettings("12345", 7, 41);

        if (certificate)
        {
            profile.SetCertificate("pfx", "senha", "CN=LOJA", DateTimeOffset.UtcNow.AddYears(1));
        }

        return profile;
    }

    [Fact]
    public void Id_da_dps_tem_45_posicoes_no_layout_nacional()
    {
        var id = NfseXml.DpsId("3118601", "11.444.777/0001-61", 7, 41);

        id.Length.ShouldBe(45);
        id.ShouldBe("DPS3118601211444777000161" + "00007" + "000000000000041");
    }

    [Fact]
    public void Dps_sai_com_prestador_tomador_servico_e_iss()
    {
        var profile = Profile();
        var note = new ServiceInvoice(Tenant, Store, "os-1", 7, 41, 2, NfseXml.DpsId("3118601", profile.Cnpj, 7, 41), "11444777000161", "Cliente SA", "010101", "Dev", 150000, 3m, true, new DateOnly(2026, 11, 12), DateTimeOffset.UtcNow);

        var xml = XDocument.Parse(NfseXml.Dps(profile, note, Request(withheld: true), new DateTimeOffset(2026, 11, 12, 13, 0, 0, TimeSpan.Zero)));
        var info = xml.Root!.Element(Ns + "infDPS")!;

        xml.Root.Attribute("versao")!.Value.ShouldBe("1.00");
        info.Attribute("Id")!.Value.ShouldBe(note.DpsId);
        info.Element(Ns + "tpAmb")!.Value.ShouldBe("2");
        info.Element(Ns + "dhEmi")!.Value.ShouldBe("2026-11-12T10:00:00-03:00");
        info.Element(Ns + "nDPS")!.Value.ShouldBe("41");
        info.Element(Ns + "prest")!.Element(Ns + "IM")!.Value.ShouldBe("12345");
        info.Element(Ns + "prest")!.Element(Ns + "regTrib")!.Element(Ns + "opSimpNac")!.Value.ShouldBe("1");
        info.Element(Ns + "toma")!.Element(Ns + "CNPJ")!.Value.ShouldBe("11444777000161");
        info.Element(Ns + "toma")!.Element(Ns + "end")!.Element(Ns + "endNac")!.Element(Ns + "CEP")!.Value.ShouldBe("32000000");
        info.Element(Ns + "serv")!.Element(Ns + "cServ")!.Element(Ns + "cTribNac")!.Value.ShouldBe("010101");
        var values = info.Element(Ns + "valores")!;
        values.Element(Ns + "vServPrest")!.Element(Ns + "vServ")!.Value.ShouldBe("1500.00");
        values.Descendants(Ns + "tpRetISSQN").Single().Value.ShouldBe("2");
        values.Descendants(Ns + "pAliq").Single().Value.ShouldBe("3.00");
    }

    [Fact]
    public void Sem_aliquota_o_pAliq_fica_de_fora_e_cpf_vira_CPF()
    {
        var profile = Profile();
        var request = Request(rate: null) with { Taker = new ServiceTaker("52998224725", "Fulano") };
        var note = new ServiceInvoice(Tenant, Store, "os-2", 1, 1, 2, NfseXml.DpsId("3118601", profile.Cnpj, 1, 1), "52998224725", "Fulano", "010101", "Dev", 1000, null, false, new DateOnly(2026, 11, 12), DateTimeOffset.UtcNow);

        var xml = XDocument.Parse(NfseXml.Dps(profile, note, request, DateTimeOffset.UtcNow));

        xml.Descendants(Ns + "pAliq").ShouldBeEmpty();
        xml.Descendants(Ns + "CPF").Single().Value.ShouldBe("52998224725");
        xml.Descendants(Ns + "end").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("01010", "cTribNac")]
    [InlineData("abcdef", "cTribNac")]
    public void Codigo_de_servico_invalido_e_recusado(string code, string expected) =>
        NfseXml.Validate(Request() with { ServiceCode = code })!.ShouldContain(expected);

    [Fact]
    public void Aliquota_fora_de_2_a_5_e_recusada() => NfseXml.Validate(Request(rate: 6m))!.ShouldContain("2% a 5%");

    [Fact]
    public void Tomador_com_documento_invalido_e_recusado() =>
        NfseXml.Validate(Request() with { Taker = new ServiceTaker("11111111111", "X") })!.ShouldContain("tomador");

    [Fact]
    public void Evento_de_cancelamento_tem_id_PRE_mais_59_digitos()
    {
        var key = new string('3', 50);

        var xml = XDocument.Parse(NfseXml.CancelEvent(2, "11444777000161", key, "Serviço não foi prestado ao cliente", DateTimeOffset.UtcNow));

        var id = xml.Root!.Element(Ns + "infPedReg")!.Attribute("Id")!.Value;
        id.ShouldBe($"PRE{key}101101001");
        xml.Descendants(Ns + "e101101").Single().Element(Ns + "cMotivo")!.Value.ShouldBe("1");
    }

    [Fact]
    public void Assinatura_confere_com_o_certificado()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=LOJA TESTE LTDA:11444777000161", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var profile = Profile();
        var note = new ServiceInvoice(Tenant, Store, "os-1", 7, 41, 2, NfseXml.DpsId("3118601", profile.Cnpj, 7, 41), "11444777000161", "Cliente SA", "010101", "Dev", 150000, 3m, false, new DateOnly(2026, 11, 12), DateTimeOffset.UtcNow);

        var signed = NationalNfseGateway.Sign(NfseXml.Dps(profile, note, Request(), DateTimeOffset.UtcNow), "infDPS", certificate);

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(signed);
        var check = new SignedXml(document);
        check.LoadXml((XmlElement)document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);
        check.CheckSignature(certificate, verifySignatureOnly: true).ShouldBeTrue();
        check.SignedInfo!.SignatureMethod.ShouldBe(SignedXml.XmlDsigRSASHA256Url);
        ((Reference)check.SignedInfo.References[0]!).Uri.ShouldBe($"#{note.DpsId}");
    }

    [Fact]
    public void Compacta_e_descompacta_e_le_erros_da_api()
    {
        NationalNfseGateway.Unpack(NationalNfseGateway.Pack("<a>ç</a>")).ShouldBe("<a>ç</a>");
        NationalNfseGateway.Errors("""{"erros":[{"Codigo":"E0712","Descricao":"Código de tributação inexistente","Complemento":"010199"}]}""")
            .ShouldBe("E0712 - Código de tributação inexistente - 010199");
    }

    // ---------- Service ----------

    private sealed class FakeGateway(params NfseResult[] answers) : INfseGateway
    {
        private readonly Queue<NfseResult> _answers = new(answers);

        public List<string> Sent { get; } = [];

        public Task<NfseResult> IssueAsync(int environment, string dpsXml, DfeCredentials credentials, CancellationToken cancellationToken)
        {
            Sent.Add(dpsXml);
            return Task.FromResult(_answers.Dequeue());
        }

        public Task<DfeEventResult> CancelAsync(int environment, string accessKey, string eventXml, DfeCredentials credentials, CancellationToken cancellationToken) =>
            Task.FromResult(new DfeEventResult(true, "ok", null, "<evento/>"));
    }

    private static (NfseService Service, FiscalProfile Profile, MemoryRepository<ServiceInvoice> Notes) Service(FakeGateway gateway, bool certificate = true)
    {
        var profile = Profile(certificate);
        var protector = Substitute.For<ISecretProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(call => call.Arg<string>() == "pfx" ? Convert.ToBase64String([1, 2, 3]) : "senha");
        var notes = new MemoryRepository<ServiceInvoice>();
        return (new NfseService(new MemoryRepository<FiscalProfile>(profile), notes, gateway, protector, Substitute.For<IUnitOfWork>(), TimeProvider.System), profile, notes);
    }

    [Fact]
    public async Task Autorizada_guarda_a_chave_e_repetir_o_id_nao_emite_de_novo()
    {
        var gateway = new FakeGateway(new NfseResult(DfeOutcome.Authorized, "ok", new string('3', 50), "<NFSe/>"));
        var (service, profile, _) = Service(gateway);

        var first = await service.IssueAsync(Store, Request(), false, CancellationToken.None);
        var again = await service.IssueAsync(Store, Request(), false, CancellationToken.None);

        first.Value.Status.ShouldBe("authorized");
        first.Value.AccessKey.ShouldBe(new string('3', 50));
        first.Value.Number.ShouldBe(41);
        first.Value.DanfseUrl.ShouldNotBeNull();
        again.Value.Id.ShouldBe(first.Value.Id);
        gateway.Sent.Count.ShouldBe(1);
        profile.NextServiceNumber.ShouldBe(42);
    }

    [Fact]
    public async Task Recusada_e_reenviada_usa_o_mesmo_numero_da_dps()
    {
        var gateway = new FakeGateway(
            new NfseResult(DfeOutcome.Rejected, "E0712 - Código de tributação inexistente"),
            new NfseResult(DfeOutcome.Authorized, "ok", new string('4', 50)));
        var (service, profile, _) = Service(gateway);

        var refused = await service.IssueAsync(Store, Request(), false, CancellationToken.None);
        var retried = await service.IssueAsync(Store, Request(), false, CancellationToken.None);

        refused.Value.Status.ShouldBe("rejected");
        retried.Value.Status.ShouldBe("authorized");
        retried.Value.Number.ShouldBe(refused.Value.Number);
        profile.NextServiceNumber.ShouldBe(42);
    }

    [Fact]
    public async Task Simulacao_nao_chama_o_governo_nem_gasta_numero()
    {
        var gateway = new FakeGateway();
        var (service, profile, _) = Service(gateway, certificate: false);

        var note = await service.IssueAsync(Store, Request(), true, CancellationToken.None);

        note.Value.Status.ShouldBe("authorized");
        note.Value.Simulation.ShouldBeTrue();
        note.Value.AccessKey!.Length.ShouldBe(50);
        note.Value.DanfseUrl.ShouldBeNull();
        gateway.Sent.ShouldBeEmpty();
        profile.NextServiceNumber.ShouldBe(41);
        (await service.ListAsync(Store, false, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sem_certificado_nao_emite_de_verdade()
    {
        var (service, _, _) = Service(new FakeGateway(), certificate: false);

        var result = await service.IssueAsync(Store, Request(), false, CancellationToken.None);

        result.Error.Code.ShouldBe("nfse.no_certificate");
    }

    [Fact]
    public async Task Cancela_nfse_autorizada()
    {
        var (service, _, _) = Service(new FakeGateway(new NfseResult(DfeOutcome.Authorized, "ok", new string('3', 50), "<NFSe/>")));
        var note = await service.IssueAsync(Store, Request(), false, CancellationToken.None);

        var cancelled = await service.CancelAsync(Store, note.Value.Id, new CancelServiceNoteRequest("Serviço não foi prestado ao cliente"), false, CancellationToken.None);

        cancelled.Value.Status.ShouldBe("cancelled");
    }
}
