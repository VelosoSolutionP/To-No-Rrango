using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Parties;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Fiscal;

public sealed record NfeXmlRequest(string Xml);

public sealed record NfeItem(
    string Code,
    string Description,
    string? Ncm,
    string? Cfop,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    Guid? SuggestedStockItemId);

public sealed record NfePreview(
    string AccessKey,
    string Number,
    DateOnly IssuedOn,
    string SupplierCnpj,
    string SupplierName,
    string? SupplierTradeName,
    Guid? SupplierId,
    string? RecipientDocument,
    decimal Total,
    bool AlreadyImported,
    IReadOnlyList<NfeItem> Items);

public sealed record ParsedNfe(
    string AccessKey,
    string Number,
    DateOnly IssuedOn,
    string SupplierCnpj,
    string SupplierName,
    string? SupplierTradeName,
    string? RecipientDocument,
    decimal Total,
    IReadOnlyList<NfeItem> Items);

public static class NfeXmlParser
{
    private const int MaxLength = 5_000_000;
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    /// <summary>Opens the note XML with external entities and DTDs refused, because the file comes from outside.</summary>
    public static XElement ReadInfo(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxLength)
        {
            throw new ArgumentException("Arquivo XML vazio ou grande demais.");
        }

        XDocument document;

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new ArgumentException("O arquivo não é um XML válido.", exception);
        }

        return document.Descendants(Ns + "infNFe").FirstOrDefault()
            ?? throw new ArgumentException("O XML não é uma NF-e.");
    }

    /// <summary>Reads an authorized NF-e. External entities and DTDs are refused because the file comes from outside.</summary>
    public static ParsedNfe Parse(string xml)
    {
        var info = ReadInfo(xml);

        var accessKey = PhoneNumber.Digits(info.Attribute("Id")?.Value);

        if (accessKey.Length != 44)
        {
            throw new ArgumentException("A chave de acesso da NF-e é inválida.");
        }

        var header = Required(info, "ide");
        var issuer = Required(info, "emit");
        var issuedAt = header.Element(Ns + "dhEmi")?.Value ?? header.Element(Ns + "dEmi")?.Value
            ?? throw new ArgumentException("A NF-e não tem data de emissão.");
        var recipient = info.Element(Ns + "dest");

        var items = info.Elements(Ns + "det")
            .Select(detail => Required(detail, "prod"))
            .Select(product => new NfeItem(
                Text(product, "cProd"),
                Text(product, "xProd"),
                product.Element(Ns + "NCM")?.Value,
                product.Element(Ns + "CFOP")?.Value,
                Text(product, "uCom"),
                Number(product, "qCom"),
                Number(product, "vUnCom"),
                Number(product, "vProd"),
                null))
            .ToList();

        if (items.Count == 0)
        {
            throw new ArgumentException("A NF-e não possui itens.");
        }

        return new ParsedNfe(
            accessKey,
            Text(header, "nNF"),
            DateOnly.FromDateTime(DateTimeOffset.Parse(issuedAt, CultureInfo.InvariantCulture).Date),
            Text(issuer, "CNPJ"),
            Text(issuer, "xNome"),
            issuer.Element(Ns + "xFant")?.Value,
            recipient?.Element(Ns + "CNPJ")?.Value ?? recipient?.Element(Ns + "CPF")?.Value,
            Number(Required(Required(info, "total"), "ICMSTot"), "vNF"),
            items);
    }

    private static XElement Required(XElement parent, string name) =>
        parent.Element(Ns + name) ?? throw new ArgumentException($"A NF-e não tem o grupo {name}.");

    private static string Text(XElement parent, string name) => Required(parent, name).Value.Trim();

    private static decimal Number(XElement parent, string name)
    {
        return decimal.TryParse(Text(parent, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"O campo {name} da NF-e é inválido.");
    }
}

/// <summary>Builds a preview for a person to confirm; nothing is stocked until the purchase is posted.</summary>
public sealed class NfeImportService(
    IRepository<Supplier> suppliers,
    IRepository<StockItem> stockItems,
    IRepository<PurchaseEntry> purchases,
    IEstablishmentRepository establishments)
{
    public Task<Result<NfePreview>> PreviewAsync(Guid establishmentId, NfeXmlRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<NfePreview>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<NfePreview>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var parsed = NfeXmlParser.Parse(request.Xml);

            var supplier = await suppliers.FirstOrDefaultAsync(
                item => item.EstablishmentId == establishmentId && item.Document == parsed.SupplierCnpj,
                cancellationToken);

            var stock = await stockItems.ListAsync(item => item.EstablishmentId == establishmentId && item.IsActive, item => item.Name, cancellationToken, take: 5000);

            var imported = await purchases.AnyAsync(
                item => item.EstablishmentId == establishmentId && item.AccessKey == parsed.AccessKey,
                cancellationToken);

            var items = parsed.Items
                .Select(item => item with
                {
                    SuggestedStockItemId = stock.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, item.Description, StringComparison.OrdinalIgnoreCase))?.Id
                })
                .ToList();

            return new NfePreview(
                parsed.AccessKey,
                parsed.Number,
                parsed.IssuedOn,
                parsed.SupplierCnpj,
                parsed.SupplierName,
                parsed.SupplierTradeName,
                supplier?.Id,
                parsed.RecipientDocument,
                parsed.Total,
                imported,
                items);
        });
}
