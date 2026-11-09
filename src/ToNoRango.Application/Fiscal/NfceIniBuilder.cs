using System.Globalization;
using System.Text;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Application.Fiscal;

public sealed record NfceLine(string Code, string Description, int Quantity, long TotalInCents, ProductFiscalData Tax);

/// <summary>Writes the INI that ACBrMonitor turns into an NFC-e (model 65, Lucro Presumido or Real).</summary>
public static class NfceIniBuilder
{
    public const int HomologationEnvironment = 2;
    public const string HomologationDescription = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    private const int RegimeNormal = 3;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Build(
        FiscalProfile profile,
        FiscalDocument document,
        Order order,
        IReadOnlyList<NfceLine> lines,
        DateTime issuedAtLocal)
    {
        var text = new StringBuilder();

        void Section(string name) => text.AppendLine($"[{name}]");
        void Key(string name, object? value) => text.AppendLine($"{name}={Format(value)}");

        var homologation = document.Environment == HomologationEnvironment;
        var freight = Allocate(order.DeliveryFeeInCents, lines);
        var productsTotal = lines.Sum(line => line.TotalInCents);
        var isDelivery = order.FulfillmentType == FulfillmentType.Delivery;

        Section("infNFe");
        Key("versao", "4.00");

        Section("Identificacao");
        Key("cNF", document.NumericCode);
        Key("natOp", "VENDA");
        Key("mod", FiscalDocument.NfceModel);
        Key("serie", document.Series);
        Key("nNF", document.Number);
        Key("dhEmi", issuedAtLocal.ToString("dd/MM/yyyy HH:mm:ss", Invariant));
        Key("tpNF", 1);
        Key("idDest", 1);
        Key("cMunFG", profile.CityIbgeCode);
        Key("tpImp", 4);
        Key("tpEmis", 1);
        Key("tpAmb", document.Environment);
        Key("finNFe", 1);
        Key("indFinal", 1);
        Key("indPres", isDelivery ? 4 : 1);
        Key("procEmi", 0);

        Section("Emitente");
        Key("CNPJCPF", profile.Cnpj);
        Key("xNome", profile.LegalName);
        Key("xFant", profile.TradeName ?? profile.LegalName);
        Key("IE", profile.StateRegistration);
        Key("CRT", RegimeNormal);
        Key("xLgr", profile.Street);
        Key("nro", profile.Number);
        Key("xBairro", profile.District);
        Key("cMun", profile.CityIbgeCode);
        Key("xMun", profile.City);
        Key("UF", profile.State);
        Key("CEP", profile.PostalCode);
        Key("cPais", 1058);
        Key("xPais", "BRASIL");

        long icmsBaseTotal = 0, icmsTotal = 0, pisTotal = 0, cofinsTotal = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var taxBase = line.TotalInCents + freight[index];
            var suffix = (index + 1).ToString("000", Invariant);

            Section($"Produto{suffix}");
            Key("cProd", line.Code);
            Key("cEAN", "SEM GTIN");
            Key("xProd", homologation && index == 0 ? HomologationDescription : line.Description);
            Key("NCM", line.Tax.Ncm);

            if (line.Tax.Cest is not null)
            {
                Key("CEST", line.Tax.Cest);
            }

            Key("CFOP", line.Tax.Cfop);
            Key("uCom", "UN");
            Key("qCom", line.Quantity);
            Key("vUnCom", UnitValue(line));
            Key("vProd", Money(line.TotalInCents));
            Key("cEANTrib", "SEM GTIN");
            Key("uTrib", "UN");
            Key("qTrib", line.Quantity);
            Key("vUnTrib", UnitValue(line));

            if (freight[index] > 0)
            {
                Key("vFrete", Money(freight[index]));
            }

            Key("indTot", 1);

            Section($"ICMS{suffix}");
            Key("orig", 0);
            Key("CST", line.Tax.IcmsCst);

            if (line.Tax.IcmsCst == "00")
            {
                var icms = Percent(taxBase, line.Tax.IcmsRate);
                icmsBaseTotal += taxBase;
                icmsTotal += icms;
                Key("modBC", 3);
                Key("vBC", Money(taxBase));
                Key("pICMS", line.Tax.IcmsRate);
                Key("vICMS", Money(icms));
            }

            Section($"PIS{suffix}");
            Key("CST", line.Tax.PisCofinsCst);

            if (line.Tax.HasPisCofinsValues)
            {
                var pis = Percent(taxBase, line.Tax.PisRate);
                pisTotal += pis;
                Key("vBC", Money(taxBase));
                Key("pPIS", line.Tax.PisRate);
                Key("vPIS", Money(pis));
            }

            Section($"COFINS{suffix}");
            Key("CST", line.Tax.PisCofinsCst);

            if (line.Tax.HasPisCofinsValues)
            {
                var cofins = Percent(taxBase, line.Tax.CofinsRate);
                cofinsTotal += cofins;
                Key("vBC", Money(taxBase));
                Key("pCOFINS", line.Tax.CofinsRate);
                Key("vCOFINS", Money(cofins));
            }
        }

        var total = productsTotal + order.DeliveryFeeInCents;

        Section("Total");
        Key("vBC", Money(icmsBaseTotal));
        Key("vICMS", Money(icmsTotal));
        Key("vProd", Money(productsTotal));
        Key("vFrete", Money(order.DeliveryFeeInCents));
        Key("vPIS", Money(pisTotal));
        Key("vCOFINS", Money(cofinsTotal));
        Key("vNF", Money(total));

        Section("Transportador");
        Key("modFrete", order.DeliveryFeeInCents > 0 ? 0 : 9);

        Section("pag001");
        var (code, description) = PaymentCode(order.PaymentMethod);
        Key("tPag", code);

        if (description is not null)
        {
            Key("xPag", description);
        }

        Key("vPag", Money(total));

        return text.ToString();
    }

    /// <summary>Splits the delivery fee over the lines; the last one takes the rounding difference.</summary>
    private static long[] Allocate(long amount, IReadOnlyList<NfceLine> lines)
    {
        var shares = new long[lines.Count];
        var productsTotal = lines.Sum(line => line.TotalInCents);

        if (amount <= 0 || productsTotal <= 0)
        {
            return shares;
        }

        var remaining = amount;

        for (var index = 0; index < lines.Count - 1; index++)
        {
            shares[index] = amount * lines[index].TotalInCents / productsTotal;
            remaining -= shares[index];
        }

        shares[^1] = remaining;
        return shares;
    }

    /// <summary>Online and card-on-delivery orders do not record the card type, so nothing is guessed.</summary>
    private static (string Code, string? Description) PaymentCode(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => ("01", null),
        PaymentMethod.Pix => ("17", null),
        PaymentMethod.CardOnDelivery => ("99", "Cartao na entrega"),
        _ => ("99", "Pagamento online"),
    };

    private static decimal Money(long cents) => cents / 100m;

    private static long Percent(long cents, decimal rate) =>
        (long)decimal.Round(cents * rate / 100m, 0, MidpointRounding.AwayFromZero);

    private static decimal UnitValue(NfceLine line) =>
        decimal.Round(line.TotalInCents / 100m / line.Quantity, 10);

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        decimal number => number.ToString("0.00########", Invariant),
        string text => text.Replace('\r', ' ').Replace('\n', ' '),
        _ => Convert.ToString(value, Invariant) ?? string.Empty,
    };
}
