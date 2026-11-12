using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Fiscal;

namespace ToNoRango.Tests.Modules;

public class FiscalCompletoTests
{
    private static readonly NfeRecipientRequest Supplier = new("11444777000161", "FORNECEDOR LTDA", null, "Rua B", "20", "Centro", "3550308", "Sao Paulo", "SP", "01001000", null);
    private const string OriginKey = "35261111444777000161550010000012341000012345";

    private static ManualNoteLine Line(string cfop = "6202", string ncm = "21069090") =>
        new("A1", "Acai 10L", 2, 18000, ncm, cfop, "00", 12m, "01", 1.65m, 7.6m);

    private static ManualNoteRequest Note(int purpose = 4, bool entry = false, string? key = OriginKey, string cfop = "6202", string ncm = "21069090") =>
        new(purpose, "DEVOLUCAO DE COMPRA", entry, key, Supplier, [Line(cfop, ncm)], null);

    [Fact]
    public void NotaAvulsa_DevolucaoValida() => Assert.Null(DfeService.ValidateManual(Note()));

    [Fact]
    public void NotaAvulsa_DevolucaoSemChaveDeOrigem() =>
        Assert.Contains("chave", DfeService.ValidateManual(Note(key: null)));

    [Fact]
    public void NotaAvulsa_AjustePodeSairSemChave() =>
        Assert.Null(DfeService.ValidateManual(Note(purpose: 3, key: null, cfop: "5949")));

    [Fact]
    public void NotaAvulsa_CfopForaDaTabela() =>
        Assert.Contains("não existe", DfeService.ValidateManual(Note(cfop: "5999")));

    [Fact]
    public void NotaAvulsa_CfopDeEntradaEmNotaDeSaida() =>
        Assert.Contains("não combina", DfeService.ValidateManual(Note(cfop: "2202")));

    [Fact]
    public void NotaAvulsa_NcmComOitoDigitos() =>
        Assert.Contains("NCM", DfeService.ValidateManual(Note(ncm: "2106")));

    [Fact]
    public void NotaAvulsa_FinalidadeNormalNaoEhAvulsa() =>
        Assert.NotNull(DfeService.ValidateManual(Note(purpose: 1)));

    [Theory]
    [InlineData("5102")]
    [InlineData("6102")]
    [InlineData("1102")]
    [InlineData("2102")]
    [InlineData("5405")]
    [InlineData("6108")]
    [InlineData("5949")]
    [InlineData("1202")]
    [InlineData("5929")]
    [InlineData("6929")]
    [InlineData("7101")]
    public void Tabela_TemOsCfopsDoDiaADia(string cfop) => Assert.True(FiscalTables.IsCfop(cfop));

    [Theory]
    [InlineData("5999")]
    [InlineData("4102")]
    [InlineData("")]
    [InlineData(null)]
    public void Tabela_RecusaCfopInexistente(string? cfop) => Assert.False(FiscalTables.IsCfop(cfop));

    [Fact]
    public void Tabela_CodigosUnicosEOrdenados()
    {
        var codes = FiscalTables.Cfop.Select(item => item.Code).ToList();
        Assert.Equal(codes.Distinct().Count(), codes.Count);
        Assert.Equal(codes.OrderBy(code => code, StringComparer.Ordinal), codes);
        Assert.True(codes.Count > 400);
        Assert.Contains(FiscalTables.Csosn, item => item.Code == "102");
        Assert.Contains(FiscalTables.IcmsCst, item => item.Code == "60");
        Assert.Equal(9, FiscalTables.Origin.Count);
    }

    private static FiscalDocument AuthorizedNfe()
    {
        var document = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "P1", 1, 10, 2, 1000, DateTimeOffset.UtcNow, FiscalDocument.NfeModel);
        document.Authorize(new string('1', 44), "135000000000001", "<nfeProc/>", "ok", DateTimeOffset.UtcNow);
        return document;
    }

    [Fact]
    public void CartaDeCorrecao_SequenciaSobeEGuardaOTexto()
    {
        var document = AuthorizedNfe();
        document.Correct(1, "Corrige endereco de entrega para Rua das Flores 100", "135000000000002", "<evento/>", DateTimeOffset.UtcNow);
        document.Correct(2, "Corrige complemento para fundos, casa 2", "135000000000003", null, DateTimeOffset.UtcNow);

        Assert.Equal(2, document.CorrectionSequence);
        Assert.Equal("Corrige complemento para fundos, casa 2", document.CorrectionText);
        Assert.Contains("carta de correcao 1", document.Xml);
    }

    [Fact]
    public void CartaDeCorrecao_ForaDeOrdemOuEmNfceRecusa()
    {
        var document = AuthorizedNfe();
        Assert.Throws<InvalidOperationException>(() => document.Correct(2, "Texto de correcao valido aqui", "1", null, DateTimeOffset.UtcNow));

        var nfce = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "P2", 1, 11, 2, 1000, DateTimeOffset.UtcNow);
        nfce.Authorize(new string('2', 44), "1", "<x/>", "ok", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => nfce.Correct(1, "Texto de correcao valido aqui", "1", null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NotaRecebida_ResumoViraCompletaEDepoisImportada()
    {
        var note = new ReceivedNote(Guid.NewGuid(), Guid.NewGuid(), 10, OriginKey, "11444777000161", "FORNECEDOR LTDA", DateTimeOffset.UtcNow, 36000, 1, null, DateTimeOffset.UtcNow);
        Assert.Equal(ReceivedNoteStatus.Summary, note.Status);

        note.AttachXml(12, "<nfeProc/>", DateTimeOffset.UtcNow);
        Assert.Equal(ReceivedNoteStatus.Full, note.Status);
        Assert.Equal(12, note.Nsu);

        note.RecordEvent("Cancelamento", cancelled: true, DateTimeOffset.UtcNow);
        Assert.Equal(3, note.Situation);

        note.MarkImported(DateTimeOffset.UtcNow);
        Assert.Equal(ReceivedNoteStatus.Imported, note.Status);
    }
}
