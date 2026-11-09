using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Infrastructure.Integrations;

namespace ToNoRango.Tests.Modules;

public class FiscalTests
{
    private static ProductFiscalData Tax(string ncm = "21069090", string cfop = "5102", string icms = "00", decimal icmsRate = 18m, string pis = "01", decimal pisRate = 0.65m, decimal cofinsRate = 3m)
    {
        var data = new ProductFiscalData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        data.Update(ncm, cfop, null, icms, icmsRate, pis, pisRate, cofinsRate);
        return data;
    }

    [Theory]
    [InlineData("123", "5102", "00", 18, "01", 0.65, 3)]
    [InlineData("21069090", "6102", "00", 18, "01", 0.65, 3)]
    [InlineData("21069090", "5102", "102", 18, "01", 0.65, 3)]
    [InlineData("21069090", "5102", "00", 0, "01", 0.65, 3)]
    [InlineData("21069090", "5102", "60", 18, "01", 0.65, 3)]
    [InlineData("21069090", "5102", "00", 18, "01", 0, 3)]
    [InlineData("21069090", "5102", "00", 18, "06", 0.65, 3)]
    public void Product_with_invalid_tax_data_is_refused(string ncm, string cfop, string icms, double icmsRate, string pis, double pisRate, double cofinsRate) =>
        Assert.Throws<ArgumentException>(() => Tax(ncm, cfop, icms, (decimal)icmsRate, pis, (decimal)pisRate, (decimal)cofinsRate));

    [Fact]
    public void Product_outside_icms_and_pis_cofins_needs_no_rate()
    {
        var data = Tax(icms: "60", icmsRate: 0, pis: "06", pisRate: 0, cofinsRate: 0);
        Assert.False(data.HasPisCofinsValues);
    }

    [Fact]
    public void Product_tax_data_accepts_dotted_ncm() => Assert.Equal("21069090", Tax(ncm: "2106.90.90").Ncm);

    [Fact]
    public void Profile_refuses_invalid_cnpj()
    {
        var profile = new FiscalProfile(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => profile.Update("Loja", null, "11111111111111", "123456789", "Rua", "1", "Centro", "Contagem", "MG", "32000000", "3118601", 1, 1));
    }

    [Fact]
    public void Retry_keeps_number_and_access_code()
    {
        var now = DateTimeOffset.UtcNow;
        var document = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ABC", 1, 7, 2, 1000, now);
        var code = document.NumericCode;

        document.MarkUnanswered("sem resposta", now);
        document.Restart(2, 1000, now);

        Assert.Equal(7, document.Number);
        Assert.Equal(code, document.NumericCode);
        Assert.Equal(FiscalDocumentStatus.Processing, document.Status);
        Assert.InRange(code, 10_000_000, 99_999_999);
    }

    [Fact]
    public void Authorized_note_cannot_be_issued_again()
    {
        var now = DateTimeOffset.UtcNow;
        var document = new FiscalDocument(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ABC", 1, 7, 2, 1000, now);
        document.Authorize("key", "prot", "<xml/>", "ok", now);
        Assert.Throws<InvalidOperationException>(() => document.Restart(2, 1000, now));
    }

    [Fact]
    public void Monitor_authorization_is_read()
    {
        var result = AcbrFiscalGateway.ReadAnswer("OK: [NFe1]\r\nchNFe=31260000000000000000650010000000071000000000\r\nnProt=131260000000000\r\ncStat=100\r\nxMotivo=Autorizado o uso da NF-e\r\nXML=<nfeProc/>");
        Assert.Equal(FiscalIssueOutcome.Authorized, result.Outcome);
        Assert.Equal("131260000000000", result.Protocol);
        Assert.Equal("<nfeProc/>", result.Xml);
    }

    [Theory]
    [InlineData("OK: [Retorno]\r\ncStat=539\r\nxMotivo=Duplicidade", "539: Duplicidade")]
    [InlineData("ERRO: Certificado vencido", "Certificado vencido")]
    public void Monitor_rejection_keeps_the_reason(string answer, string expected)
    {
        var result = AcbrFiscalGateway.ReadAnswer(answer);
        Assert.Equal(FiscalIssueOutcome.Rejected, result.Outcome);
        Assert.Equal(expected, result.Message);
    }

    private const string EntryXml = """
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe><infNFe Id="NFe31261111222333000181550010000000421000000421" versao="4.00">
<ide><mod>55</mod><serie>1</serie><nNF>42</nNF><dhEmi>2026-11-05T10:00:00-03:00</dhEmi></ide>
<emit><CNPJ>11222333000181</CNPJ><xNome>Fornecedor Açaí</xNome><enderEmit><xLgr>Rua B</xLgr><nro>5</nro><xBairro>Centro</xBairro><cMun>3118601</cMun><UF>MG</UF></enderEmit><IE>1234567</IE></emit>
<det nItem="1"><prod><cProd>A1</cProd><xProd>Polpa</xProd><NCM>20089900</NCM><CFOP>5102</CFOP><uCom>KG</uCom><qCom>10.000</qCom><vUnCom>10</vUnCom><vProd>100.00</vProd></prod>
<imposto><ICMS><ICMS00><orig>0</orig><CST>00</CST><vBC>100.00</vBC><pICMS>18.00</pICMS><vICMS>18.00</vICMS></ICMS00></ICMS><PIS><PISAliq><CST>01</CST><vBC>100.00</vBC><pPIS>0.65</pPIS><vPIS>0.65</vPIS></PISAliq></PIS><COFINS><COFINSAliq><CST>01</CST><vBC>100.00</vBC><pCOFINS>3.00</pCOFINS><vCOFINS>3.00</vCOFINS></COFINSAliq></COFINS></imposto></det>
<total><ICMSTot><vBC>100.00</vBC><vICMS>18.00</vICMS><vBCST>0.00</vBCST><vST>0.00</vST><vProd>100.00</vProd><vFrete>0.00</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vIPI>0.00</vIPI><vPIS>0.65</vPIS><vCOFINS>3.00</vCOFINS><vOutro>0.00</vOutro><vNF>100.00</vNF></ICMSTot></total>
<transp><modFrete>9</modFrete></transp></infNFe></NFe></nfeProc>
""";

    private static FiscalProfile Profile()
    {
        var profile = new FiscalProfile(Guid.NewGuid(), Guid.NewGuid());
        profile.SetRegime(TaxRegime.LucroPresumido);
        profile.Update("Loja Teste", null, "11222333000181", "1234567", "Rua A", "10", "Centro", "Contagem", "MG", "32000000", "3118601", 1, 1);
        profile.SetAccounting("Contador", "52998224725", "MG1234", "c@x.com", "B");
        return profile;
    }

    [Fact]
    public void Regime_outside_presumido_and_real_is_cut() =>
        Assert.Throws<ArgumentException>(() => new FiscalProfile(Guid.NewGuid(), Guid.NewGuid()).SetRegime((TaxRegime)99));

    [Fact]
    public void Sintegra_lines_have_the_fixed_width_and_totals()
    {
        var note = FiscalNoteReader.Read(EntryXml, isEntry: true);
        var lines = SintegraBuilder.Build(Profile(), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), [note]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.All(lines, line => Assert.Equal(126, line.Length));
        Assert.Equal(["10", "11", "50", "54", "75", "90"], lines.Select(line => line[..2]));
        Assert.Contains("9900000006", lines[^1]);
    }

    [Fact]
    public void Efd_icms_closes_blocks_and_counts_every_line()
    {
        var note = FiscalNoteReader.Read(EntryXml, isEntry: true);
        var warnings = new List<string>();
        var text = EfdBuilder.BuildIcmsIpi(Profile(), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), [note], new EfdOptions(0, null, null, "020"), warnings);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal($"|9999|{lines.Length}|", lines[^1]);
        Assert.Contains(lines, line => line.StartsWith("|C170|1|11222333000181-A1|", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "|C190|000|5102|18,00|100,00|100,00|18,00|0,00|0,00|0,00|0,00||");
        Assert.Single(lines, line => line.StartsWith("|E110|0,00|", StringComparison.Ordinal));

        foreach (var row in lines.Where(line => line.StartsWith("|9900|", StringComparison.Ordinal)))
        {
            var parts = row.Split('|');
            Assert.Equal(int.Parse(parts[3]), lines.Count(line => line.StartsWith($"|{parts[2]}|", StringComparison.Ordinal)));
        }

        var nine = lines.SkipWhile(line => !line.StartsWith("|9001|", StringComparison.Ordinal)).Count();
        Assert.Contains($"|9990|{nine}|", lines);
        var zero = lines.TakeWhile(line => !line.StartsWith("|0990|", StringComparison.Ordinal)).Count() + 1;
        Assert.Contains($"|0990|{zero}|", lines);
    }
}
