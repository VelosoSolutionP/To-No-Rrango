using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Inventory;

namespace ToNoRango.Tests.Modules;

public class FiscalBooksTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();

    private const string EntryXml = """
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe><infNFe Id="NFe31261111222333000181550010000000421000000421" versao="4.00">
<ide><mod>55</mod><serie>1</serie><nNF>42</nNF><dhEmi>2026-11-05T10:00:00-03:00</dhEmi></ide>
<emit><CNPJ>11222333000181</CNPJ><xNome>Fornecedor Açaí</xNome><enderEmit><xLgr>Rua B</xLgr><nro>5</nro><xBairro>Centro</xBairro><cMun>3118601</cMun><UF>MG</UF></enderEmit><IE>1234567</IE></emit>
<det nItem="1"><prod><cProd>A1</cProd><xProd>Polpa</xProd><NCM>20089900</NCM><CFOP>5102</CFOP><uCom>KG</uCom><qCom>10.000</qCom><vUnCom>10</vUnCom><vProd>100.00</vProd></prod>
<imposto><ICMS><ICMS00><orig>0</orig><CST>00</CST><vBC>100.00</vBC><pICMS>18.00</pICMS><vICMS>18.00</vICMS></ICMS00></ICMS><PIS><PISAliq><CST>01</CST><vBC>100.00</vBC><pPIS>0.65</pPIS><vPIS>0.65</vPIS></PISAliq></PIS><COFINS><COFINSAliq><CST>01</CST><vBC>100.00</vBC><pCOFINS>3.00</pCOFINS><vCOFINS>3.00</vCOFINS></COFINSAliq></COFINS></imposto></det>
<total><ICMSTot><vBC>100.00</vBC><vICMS>18.00</vICMS><vBCST>0.00</vBCST><vST>0.00</vST><vProd>100.00</vProd><vFrete>0.00</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vIPI>0.00</vIPI><vPIS>0.65</vPIS><vCOFINS>3.00</vCOFINS><vOutro>0.00</vOutro><vNF>100.00</vNF></ICMSTot></total>
<transp><modFrete>9</modFrete></transp></infNFe></NFe></nfeProc>
""";

    private const string ExitXml = """
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe><infNFe Id="NFe31261199888777000166650010000000071000000077" versao="4.00">
<ide><mod>65</mod><serie>1</serie><nNF>7</nNF><dhEmi>2026-11-10T12:00:00-03:00</dhEmi></ide>
<emit><CNPJ>99888777000166</CNPJ><xNome>Loja Teste</xNome><enderEmit><xLgr>Rua A</xLgr><nro>10</nro><xBairro>Centro</xBairro><cMun>3118601</cMun><UF>MG</UF></enderEmit><IE>7654321</IE></emit>
<det nItem="1"><prod><cProd>P1</cProd><xProd>Açaí 500ml</xProd><NCM>21069090</NCM><CFOP>5102</CFOP><uCom>UN</uCom><qCom>10.000</qCom><vUnCom>20</vUnCom><vProd>200.00</vProd></prod>
<imposto><ICMS><ICMS00><orig>0</orig><CST>00</CST><vBC>200.00</vBC><pICMS>18.00</pICMS><vICMS>36.00</vICMS></ICMS00></ICMS><PIS><PISAliq><CST>01</CST><vBC>200.00</vBC><pPIS>0.65</pPIS><vPIS>1.30</vPIS></PISAliq></PIS><COFINS><COFINSAliq><CST>01</CST><vBC>200.00</vBC><pCOFINS>3.00</pCOFINS><vCOFINS>6.00</vCOFINS></COFINSAliq></COFINS></imposto></det>
<total><ICMSTot><vBC>200.00</vBC><vICMS>36.00</vICMS><vBCST>0.00</vBCST><vST>0.00</vST><vProd>200.00</vProd><vFrete>0.00</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vIPI>0.00</vIPI><vPIS>1.30</vPIS><vCOFINS>6.00</vCOFINS><vOutro>0.00</vOutro><vNF>200.00</vNF></ICMSTot></total>
<transp><modFrete>9</modFrete></transp></infNFe></NFe></nfeProc>
""";

    private static FiscalBooksService Service(TaxRegime regime, bool homologation = false)
    {
        var profile = new FiscalProfile(Tenant, Store);
        profile.SetRegime(regime);
        profile.Update("Loja Teste", null, "11444777000161", "7654321", "Rua A", "10", "Centro", "Contagem", "MG", "32000000", "3118601", 1, 1);

        var purchase = new PurchaseEntry(Tenant, Store, null, "42", null, new DateOnly(2026, 11, 5), null, "dono", DateTimeOffset.UtcNow);
        purchase.AttachNfeXml(EntryXml);
        var sale = new FiscalDocument(Tenant, Store, Guid.NewGuid(), "P1", 1, 7, homologation ? 2 : 1, 20000, new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero));
        sale.Authorize(new string('7', 44), "131260000000001", ExitXml, "ok", DateTimeOffset.UtcNow);

        var source = new FiscalNoteSource(new MemoryRepository<PurchaseEntry>(purchase), new MemoryRepository<FiscalDocument>(sale));
        return new FiscalBooksService(source, new MemoryRepository<FiscalProfile>(profile), new MemoryRepository<FiscalAuditEntry>());
    }

    [Fact]
    public async Task Apuracao_presumido_cruza_debitos_e_creditos_de_icms()
    {
        var result = await Service(TaxRegime.LucroPresumido).GetAssessmentAsync(Store, new FiscalPeriodRequest(2026, 11), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var taxes = result.Value.Taxes.ToDictionary(tax => tax.Tax);
        Assert.Equal(20000, result.Value.ExitsInCents);
        Assert.Equal(10000, result.Value.EntriesInCents);
        Assert.Equal(3600, taxes["ICMS"].DebitsInCents);
        Assert.Equal(1800, taxes["ICMS"].CreditsInCents);
        Assert.Equal(1800, taxes["ICMS"].BalanceInCents);
        Assert.Equal(130, taxes["PIS"].BalanceInCents);
        Assert.Equal(600, taxes["COFINS"].BalanceInCents);
        Assert.Equal(240, taxes["IRPJ"].BalanceInCents);
        Assert.Equal(216, taxes["CSLL"].BalanceInCents);
    }

    [Fact]
    public async Task Saldo_credor_anterior_abate_o_icms()
    {
        var result = await Service(TaxRegime.LucroPresumido).GetAssessmentAsync(Store, new FiscalPeriodRequest(2026, 11, 2500), CancellationToken.None);

        var icms = result.Value.Taxes.Single(tax => tax.Tax == "ICMS");
        Assert.Equal(-700, icms.BalanceInCents);
    }

    [Fact]
    public async Task Lucro_real_estima_credito_de_pis_e_cofins_nas_compras()
    {
        var result = await Service(TaxRegime.LucroReal).GetAssessmentAsync(Store, new FiscalPeriodRequest(2026, 11), CancellationToken.None);

        var taxes = result.Value.Taxes.ToDictionary(tax => tax.Tax);
        Assert.Equal(165, taxes["PIS"].CreditsInCents);
        Assert.Equal(760, taxes["COFINS"].CreditsInCents);
        Assert.False(taxes.ContainsKey("IRPJ"));
    }

    [Fact]
    public async Task Livro_lista_entradas_e_saidas_e_consolida_por_cfop()
    {
        var result = await Service(TaxRegime.LucroPresumido).GetBookAsync(Store, new FiscalPeriodRequest(2026, 11), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Rows.Count);
        Assert.True(result.Value.Rows[0].IsEntry);
        Assert.Equal("5102", result.Value.Rows[1].Cfops);
        Assert.Equal(2, result.Value.ByCfop.Count);
        Assert.Equal(3600, result.Value.ByCfop.Single(total => !total.IsEntry).IcmsInCents);
    }

    [Fact]
    public async Task Nota_de_homologacao_fica_fora_da_apuracao()
    {
        var result = await Service(TaxRegime.LucroPresumido, homologation: true).GetAssessmentAsync(Store, new FiscalPeriodRequest(2026, 11), CancellationToken.None);

        Assert.Equal(0, result.Value.ExitsInCents);
        Assert.Contains(result.Value.Warnings, warning => warning.Contains("homologação"));
    }

    [Fact]
    public async Task Periodo_invalido_e_recusado()
    {
        var result = await Service(TaxRegime.LucroPresumido).GetBookAsync(Store, new FiscalPeriodRequest(2026, 13), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Auditoria_corta_textos_longos_e_exige_acao()
    {
        var entry = new FiscalAuditEntry(Tenant, Store, DateTimeOffset.UtcNow, "", "cancelamento", "Cancelou nota", null, "2026-11", new string('x', 1500), true, 200);

        Assert.Equal("desconhecido", entry.Actor);
        Assert.Equal(1000, entry.Details!.Length);
        Assert.Throws<ArgumentException>(() => new FiscalAuditEntry(Tenant, Store, DateTimeOffset.UtcNow, "a", "", "x", null, null, null, true, 200));
    }
}
