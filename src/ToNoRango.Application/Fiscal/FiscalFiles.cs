using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ToNoRango.Domain.Fiscal;

namespace ToNoRango.Application.Fiscal;

public sealed record NoteLine(
    int Item,
    string Code,
    string Description,
    string Ncm,
    string Cfop,
    string Unit,
    decimal Quantity,
    long Value,
    long Discount,
    long Extras,
    string IcmsCst,
    long IcmsBase,
    decimal IcmsRate,
    long Icms,
    string PisCst,
    long PisBase,
    decimal PisRate,
    long Pis,
    string CofinsCst,
    long CofinsBase,
    decimal CofinsRate,
    long Cofins);

public sealed record FiscalNote(
    bool IsEntry,
    int Model,
    string Series,
    int Number,
    string Key,
    DateOnly IssuedOn,
    string PartyCnpj,
    string PartyName,
    string PartyIe,
    string PartyUf,
    string PartyCity,
    string PartyStreet,
    string PartyNumber,
    string PartyDistrict,
    string FreightMode,
    long Products,
    long Discount,
    long Freight,
    long Insurance,
    long Other,
    long IcmsBase,
    long Icms,
    long StBase,
    long St,
    long Ipi,
    long Pis,
    long Cofins,
    long Total,
    IReadOnlyList<NoteLine> Lines);

/// <summary>Reads the note data the fiscal files need from an authorized NF-e or NFC-e XML.</summary>
public static class FiscalNoteReader
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static FiscalNote Read(string xml, bool isEntry)
    {
        var info = NfeXmlParser.ReadInfo(xml);
        var header = Child(info, "ide");
        var issuer = Child(info, "emit");
        var issuerAddress = Child(issuer, "enderEmit");
        var total = Child(Child(info, "total"), "ICMSTot");
        var issued = Text(header, "dhEmi") is { Length: >= 10 } stamp ? stamp : Text(header, "dEmi");

        var lines = info.Elements(Ns + "det").Select(ReadLine).ToList();

        return new FiscalNote(
            isEntry,
            int.Parse(Text(header, "mod"), Invariant),
            Text(header, "serie"),
            int.Parse(Text(header, "nNF"), Invariant),
            new string((info.Attribute("Id")?.Value ?? string.Empty).Where(char.IsAsciiDigit).ToArray()),
            DateOnly.ParseExact(issued[..10], "yyyy-MM-dd", Invariant),
            isEntry ? Text(issuer, "CNPJ") : string.Empty,
            isEntry ? Text(issuer, "xNome") : string.Empty,
            isEntry ? Text(issuer, "IE") : string.Empty,
            isEntry ? Text(issuerAddress, "UF") : string.Empty,
            isEntry ? Text(issuerAddress, "cMun") : string.Empty,
            isEntry ? Text(issuerAddress, "xLgr") : string.Empty,
            isEntry ? Text(issuerAddress, "nro") : string.Empty,
            isEntry ? Text(issuerAddress, "xBairro") : string.Empty,
            info.Element(Ns + "transp")?.Element(Ns + "modFrete")?.Value ?? "9",
            Money(total, "vProd"),
            Money(total, "vDesc"),
            Money(total, "vFrete"),
            Money(total, "vSeg"),
            Money(total, "vOutro"),
            Money(total, "vBC"),
            Money(total, "vICMS"),
            Money(total, "vBCST"),
            Money(total, "vST"),
            Money(total, "vIPI"),
            Money(total, "vPIS"),
            Money(total, "vCOFINS"),
            Money(total, "vNF"),
            lines);
    }

    private static NoteLine ReadLine(XElement detail)
    {
        var product = Child(detail, "prod");
        var tax = Child(detail, "imposto");
        var icms = Child(tax, "ICMS").Elements().First();
        var origin = Text(icms, "orig");
        // Fornecedor do Simples traz CSOSN; na escrituração do comprador isso vira CST 90.
        var cst = icms.Element(Ns + "CST")?.Value ?? "90";
        var (pisCst, pisBase, pisRate, pisValue) = Contribution(tax.Element(Ns + "PIS"), "pPIS", "vPIS");
        var (cofinsCst, cofinsBase, cofinsRate, cofinsValue) = Contribution(tax.Element(Ns + "COFINS"), "pCOFINS", "vCOFINS");

        return new NoteLine(
            int.Parse(detail.Attribute("nItem")?.Value ?? "1", Invariant),
            Text(product, "cProd"),
            Text(product, "xProd"),
            Text(product, "NCM"),
            Text(product, "CFOP"),
            Text(product, "uCom"),
            decimal.Parse(Text(product, "qCom"), Invariant),
            Money(product, "vProd"),
            Money(product, "vDesc"),
            Money(product, "vFrete") + Money(product, "vSeg") + Money(product, "vOutro"),
            origin + cst,
            Money(icms, "vBC"),
            Rate(icms, "pICMS"),
            Money(icms, "vICMS"),
            pisCst,
            pisBase,
            pisRate,
            pisValue,
            cofinsCst,
            cofinsBase,
            cofinsRate,
            cofinsValue);
    }

    private static (string Cst, long Base, decimal Rate, long Value) Contribution(XElement? group, string rate, string value)
    {
        var body = group?.Elements().FirstOrDefault();
        return body is null
            ? ("99", 0, 0m, 0)
            : (Text(body, "CST"), Money(body, "vBC"), Rate(body, rate), Money(body, value));
    }

    private static XElement Child(XElement parent, string name) =>
        parent.Element(Ns + name) ?? throw new ArgumentException($"A nota não tem o grupo {name}.");

    private static string Text(XElement parent, string name) => parent.Element(Ns + name)?.Value.Trim() ?? string.Empty;

    private static decimal Rate(XElement parent, string name) =>
        decimal.TryParse(parent.Element(Ns + name)?.Value, NumberStyles.Number, Invariant, out var rate) ? rate : 0m;

    private static long Money(XElement parent, string name) =>
        decimal.TryParse(parent.Element(Ns + name)?.Value, NumberStyles.Number, Invariant, out var value)
            ? (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero)
            : 0;
}

public sealed record EfdOptions(long PreviousCreditInCents, string? RevenueCode, DateOnly? DueDate, string LayoutVersion);

/// <summary>Layout of Convênio ICMS 57/95: fixed lines of 126 characters. NFC-e is not part of this file.</summary>
public static class SintegraBuilder
{
    private const int LineLength = 126;

    public static string Build(FiscalProfile profile, DateOnly start, DateOnly end, IReadOnlyList<FiscalNote> notes)
    {
        var entries = notes.Where(note => note.Model == 55).ToList();
        var lines = new List<string>
        {
            "10" + Num(profile.Cnpj, 14) + Alpha(profile.StateRegistration, 14) + Alpha(profile.LegalName, 35) + Alpha(profile.City, 30)
                + Alpha(profile.State, 2) + Num(string.Empty, 10) + Date(start) + Date(end) + "331",
            "11" + Alpha(profile.Street, 34) + Num(profile.Number, 5) + Alpha(string.Empty, 22) + Alpha(profile.District, 15) + Num(profile.PostalCode, 8)
                + Alpha(profile.AccountantName ?? profile.LegalName, 28) + Num(string.Empty, 12),
        };

        foreach (var note in entries)
        {
            foreach (var group in note.Lines.GroupBy(line => (line.Cfop, line.IcmsRate)))
            {
                lines.Add("50" + Num(note.PartyCnpj, 14) + Alpha(note.PartyIe, 14) + Date(note.IssuedOn) + Alpha(note.PartyUf, 2) + "55"
                    + Alpha(note.Series, 3) + Num(note.Number.ToString(CultureInfo.InvariantCulture), 6) + group.Key.Cfop + "T"
                    + Amount(group.Sum(OperationValue), 13) + Amount(group.Sum(line => line.IcmsBase), 13) + Amount(group.Sum(line => line.Icms), 13)
                    + Amount(0, 13) + Amount(group.Sum(line => OperationValue(line) - line.IcmsBase), 13) + Amount((long)(group.Key.IcmsRate * 100m), 4) + "N");
            }
        }

        foreach (var note in entries)
        {
            foreach (var line in note.Lines)
            {
                lines.Add("54" + Num(note.PartyCnpj, 14) + "55" + Alpha(note.Series, 3) + Num(note.Number.ToString(CultureInfo.InvariantCulture), 6) + line.Cfop
                    + Alpha(line.IcmsCst, 3) + Num(line.Item.ToString(CultureInfo.InvariantCulture), 3) + Alpha(ItemCode(note, line), 14)
                    + Amount((long)(line.Quantity * 1000m), 11) + Amount(line.Value, 12) + Amount(line.Discount, 12) + Amount(line.IcmsBase, 12)
                    + Amount(0, 12) + Amount(0, 12) + Amount((long)(line.IcmsRate * 100m), 4));
            }
        }

        foreach (var item in entries.SelectMany(note => note.Lines.Select(line => (note, line))).DistinctBy(pair => ItemCode(pair.note, pair.line)))
        {
            lines.Add("75" + Date(start) + Date(end) + Alpha(ItemCode(item.note, item.line), 14) + Alpha(item.line.Ncm, 8) + Alpha(item.line.Description, 53)
                + Alpha(item.line.Unit, 6) + Amount(0, 5) + Amount((long)(item.line.IcmsRate * 100m), 4) + Amount(0, 5) + Amount(0, 13));
        }

        var counts = lines.GroupBy(line => line[..2]).Where(group => group.Key is not ("10" or "11")).OrderBy(group => group.Key).ToList();
        var totals = new StringBuilder();

        foreach (var count in counts)
        {
            totals.Append(count.Key).Append(Num(count.Count().ToString(CultureInfo.InvariantCulture), 8));
        }

        totals.Append("99").Append(Num((lines.Count + 1).ToString(CultureInfo.InvariantCulture), 8));
        lines.Add(("90" + Num(profile.Cnpj, 14) + Alpha(profile.StateRegistration, 14) + totals).PadRight(LineLength - 1) + "1");

        return string.Join("\r\n", lines) + "\r\n";
    }

    private static long OperationValue(NoteLine line) => line.Value - line.Discount + line.Extras;

    private static string ItemCode(FiscalNote note, NoteLine line) =>
        (note.PartyCnpj[..4] + line.Code).Length > 14 ? (note.PartyCnpj[..4] + line.Code)[..14] : note.PartyCnpj[..4] + line.Code;

    private static string Date(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Num(string value, int size)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return (digits.Length > size ? digits[^size..] : digits).PadLeft(size, '0');
    }

    private static string Amount(long value, int size) => Num(Math.Abs(value).ToString(CultureInfo.InvariantCulture), size);

    private static string Alpha(string value, int size)
    {
        var plain = FiscalText.Plain(value).ToUpperInvariant();
        return (plain.Length > size ? plain[..size] : plain).PadRight(size);
    }
}

internal static class FiscalText
{
    public static string Plain(string value)
    {
        var text = new StringBuilder();

        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                text.Append(character is '\r' or '\n' or '|' ? ' ' : character);
            }
        }

        return text.ToString().Trim();
    }
}

/// <summary>Collects pipe-separated records and writes the block closings and block 9 of an EFD file.</summary>
internal sealed class EfdWriter
{
    private readonly List<string> _lines = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private int _blockStart;

    public void Add(string register, params object?[] fields)
    {
        var text = new StringBuilder("|").Append(register).Append('|');

        foreach (var field in fields)
        {
            text.Append(Format(field)).Append('|');
        }

        _lines.Add(text.ToString());
        _counts[register] = _counts.GetValueOrDefault(register) + 1;
    }

    public void CloseBlock(string register)
    {
        Add(register, _lines.Count - _blockStart + 1);
        _blockStart = _lines.Count;
    }

    public void EmptyBlock(char block)
    {
        Add($"{block}001", 1);
        CloseBlock($"{block}990");
    }

    public string Finish()
    {
        Add("9001", 0);
        var registers = _counts.Keys.Concat(["9900", "9990", "9999"]).Distinct().OrderBy(key => key, StringComparer.Ordinal).ToList();
        var ownCount = registers.Count;

        foreach (var register in registers)
        {
            var count = register switch
            {
                "9900" => ownCount,
                "9990" or "9999" => 1,
                _ => _counts[register],
            };
            Add("9900", register, count);
        }

        Add("9990", _lines.Count - _blockStart + 2);
        Add("9999", _lines.Count + 1);
        return string.Join("\r\n", _lines) + "\r\n";
    }

    // A API roda sem dados de cultura, então a vírgula do layout é aplicada aqui.
    private static string Decimal(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Format(object? field) => field switch
    {
        null => string.Empty,
        long cents => Decimal(cents / 100m),
        decimal number => Decimal(number),
        DateOnly date => date.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
        string text => FiscalText.Plain(text),
        _ => Convert.ToString(field, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}

public static class EfdBuilder
{
    private const string BasicPisCode = "51";

    public static string BuildIcmsIpi(FiscalProfile profile, DateOnly start, DateOnly end, IReadOnlyList<FiscalNote> notes, EfdOptions options, List<string> warnings)
    {
        var file = new EfdWriter();
        var entries = notes.Where(note => note.IsEntry).ToList();
        var items = entries.SelectMany(note => note.Lines.Select(line => (Code: ItemCode(note, line), Line: line))).DistinctBy(item => item.Code).ToList();

        file.Add("0000", options.LayoutVersion, 0, start, end, profile.LegalName, profile.Cnpj, null, profile.State, profile.StateRegistration, profile.CityIbgeCode, null, null, profile.SpedProfile, 1);
        file.Add("0001", 0);
        file.Add("0005", profile.TradeName ?? profile.LegalName, profile.PostalCode, profile.Street, profile.Number, null, profile.District, null, null, null);
        AddAccountant(file, profile);
        AddParticipants(file, entries);

        foreach (var unit in items.Select(item => item.Line.Unit).Distinct(StringComparer.Ordinal))
        {
            file.Add("0190", unit, unit);
        }

        foreach (var (code, line) in items)
        {
            file.Add("0200", code, line.Description, null, null, line.Unit, "00", line.Ncm, null, null, null, null, null);
        }

        file.CloseBlock("0990");
        file.EmptyBlock('B');
        file.Add("C001", notes.Count > 0 ? 0 : 1);

        foreach (var note in notes.OrderBy(note => note.IssuedOn).ThenBy(note => note.Number))
        {
            AddC100(file, note);

            if (note.IsEntry)
            {
                foreach (var line in note.Lines)
                {
                    file.Add("C170", line.Item, ItemCode(note, line), null, line.Quantity, line.Unit, line.Value, line.Discount, 0, line.IcmsCst, line.Cfop, null,
                        line.IcmsBase, line.IcmsRate, line.Icms, 0L, 0m, 0L, 0, null, null, 0L, 0m, 0L,
                        line.PisCst, line.PisBase, line.PisRate, null, null, line.Pis, line.CofinsCst, line.CofinsBase, line.CofinsRate, null, null, line.Cofins, null, 0L);
                }
            }

            foreach (var group in note.Lines.GroupBy(line => (line.IcmsCst, line.Cfop, line.IcmsRate)))
            {
                file.Add("C190", group.Key.IcmsCst, group.Key.Cfop, group.Key.IcmsRate, group.Sum(line => line.Value - line.Discount + line.Extras),
                    group.Sum(line => line.IcmsBase), group.Sum(line => line.Icms), 0L, 0L, 0L, 0L, null);
            }
        }

        file.CloseBlock("C990");
        file.EmptyBlock('D');

        var debits = notes.Where(note => !note.IsEntry).Sum(note => note.Icms);
        var credits = entries.Sum(note => note.Icms);
        var balance = debits - credits - options.PreviousCreditInCents;
        var toPay = Math.Max(balance, 0);

        file.Add("E001", 0);
        file.Add("E100", start, end);
        file.Add("E110", debits, 0L, 0L, 0L, credits, 0L, 0L, 0L, options.PreviousCreditInCents, toPay, 0L, toPay, Math.Max(-balance, 0), 0L);

        if (toPay > 0)
        {
            if (string.IsNullOrWhiteSpace(options.RevenueCode) || options.DueDate is null)
            {
                warnings.Add("Há ICMS a recolher: informe o código de receita da UF e o vencimento, ou o registro E116 sai incompleto.");
            }

            file.Add("E116", "000", toPay, options.DueDate, options.RevenueCode, null, null, null, null, start.ToString("MMyyyy", CultureInfo.InvariantCulture));
        }

        file.CloseBlock("E990");
        file.EmptyBlock('G');
        file.EmptyBlock('H');
        file.EmptyBlock('K');
        file.Add("1001", 0);
        file.Add("1010", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N");
        file.CloseBlock("1990");

        if (credits > 0)
        {
            warnings.Add("O crédito de ICMS soma todas as notas de entrada. O contador precisa retirar o que não dá direito a crédito.");
        }

        return file.Finish();
    }

    /// <summary>Cumulative regime only, which is the Lucro Presumido case.</summary>
    public static string BuildContributions(FiscalProfile profile, DateOnly start, DateOnly end, IReadOnlyList<FiscalNote> notes, List<string> warnings)
    {
        var file = new EfdWriter();
        var sales = notes.Where(note => !note.IsEntry).OrderBy(note => note.IssuedOn).ThenBy(note => note.Number).ToList();

        file.Add("0000", "006", 0, null, null, start, end, profile.LegalName, profile.Cnpj, profile.State, profile.CityIbgeCode, null, "00", 2);
        file.Add("0001", 0);
        AddAccountant(file, profile);
        file.Add("0110", 2, null, null, 9);
        file.Add("0140", "1", profile.LegalName, profile.Cnpj, profile.State, profile.StateRegistration, profile.CityIbgeCode, null, null);
        file.CloseBlock("0990");
        file.EmptyBlock('A');
        file.Add("C001", sales.Count > 0 ? 0 : 1);

        if (sales.Count > 0)
        {
            file.Add("C010", profile.Cnpj, 2);
        }

        foreach (var note in sales)
        {
            AddC100(file, note);

            foreach (var group in note.Lines.GroupBy(line => (line.Cfop, line.PisCst, line.PisRate, line.CofinsCst, line.CofinsRate)))
            {
                file.Add("C175", group.Key.Cfop, group.Sum(line => line.Value + line.Extras), group.Sum(line => line.Discount),
                    group.Key.PisCst, group.Sum(line => line.PisBase), group.Key.PisRate, null, null, group.Sum(line => line.Pis),
                    group.Key.CofinsCst, group.Sum(line => line.CofinsBase), group.Key.CofinsRate, null, null, group.Sum(line => line.Cofins), null, null);
            }
        }

        file.CloseBlock("C990");
        file.EmptyBlock('D');
        file.EmptyBlock('F');
        file.EmptyBlock('I');

        var lines = sales.SelectMany(note => note.Lines).ToList();
        var pisBase = lines.Sum(line => line.PisBase);
        var pis = lines.Sum(line => line.Pis);
        var cofinsBase = lines.Sum(line => line.CofinsBase);
        var cofins = lines.Sum(line => line.Cofins);
        var receipts = lines.Sum(line => line.Value - line.Discount + line.Extras);

        if (lines.Any(line => line.PisCst is not ("01" or "02")))
        {
            warnings.Add("Há venda com CST sem tributação de PIS/COFINS: os registros M400 e M800 precisam ser completados pelo contador.");
        }

        file.Add("M001", pisBase > 0 || cofinsBase > 0 ? 0 : 1);

        if (pisBase > 0)
        {
            file.Add("M200", 0L, 0L, 0L, 0L, 0L, 0L, 0L, pis, 0L, 0L, pis, pis);
            file.Add("M210", BasicPisCode, receipts, pisBase, 0L, 0L, pisBase, lines.First(line => line.Pis > 0).PisRate, null, null, pis, 0L, 0L, 0L, 0L, pis);
        }

        if (cofinsBase > 0)
        {
            file.Add("M600", 0L, 0L, 0L, 0L, 0L, 0L, 0L, cofins, 0L, 0L, cofins, cofins);
            file.Add("M610", BasicPisCode, receipts, cofinsBase, 0L, 0L, cofinsBase, lines.First(line => line.Cofins > 0).CofinsRate, null, null, cofins, 0L, 0L, 0L, 0L, cofins);
        }

        file.CloseBlock("M990");
        file.EmptyBlock('P');
        file.EmptyBlock('1');

        return file.Finish();
    }

    private static void AddAccountant(EfdWriter file, FiscalProfile profile) =>
        file.Add("0100", profile.AccountantName, profile.AccountantCpf, profile.AccountantCrc, null, profile.PostalCode, profile.Street, profile.Number, null, profile.District, null, null, profile.AccountantEmail, profile.CityIbgeCode);

    private static void AddParticipants(EfdWriter file, IReadOnlyList<FiscalNote> entries)
    {
        foreach (var note in entries.DistinctBy(note => note.PartyCnpj))
        {
            file.Add("0150", note.PartyCnpj, note.PartyName, "01058", note.PartyCnpj, null, note.PartyIe, note.PartyCity, null, note.PartyStreet, note.PartyNumber, null, note.PartyDistrict);
        }
    }

    private static void AddC100(EfdWriter file, FiscalNote note) =>
        file.Add("C100", note.IsEntry ? 0 : 1, note.IsEntry ? 1 : 0, note.IsEntry ? note.PartyCnpj : null, note.Model.ToString(CultureInfo.InvariantCulture), "00", note.Series,
            note.Number, note.Key, note.IssuedOn, note.IssuedOn, note.Total, 0, note.Discount, 0L, note.Products, note.FreightMode, note.Freight, note.Insurance, note.Other,
            note.IcmsBase, note.Icms, note.StBase, note.St, note.Ipi, note.Pis, note.Cofins, 0L, 0L);

    private static string ItemCode(FiscalNote note, NoteLine line) => $"{note.PartyCnpj}-{line.Code}";
}
