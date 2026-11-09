using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Inventory;

public enum StockMovementType
{
    Purchase = 1,
    SaleDeduction = 2,
    SaleReversal = 3,
    Adjustment = 4,
    Loss = 5
}

public sealed class StockItem : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public decimal QuantityOnHand { get; private set; }
    public decimal MinimumQuantity { get; private set; }
    public long AverageCostInCents { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private StockItem() { }

    public StockItem(Guid tenantId, Guid establishmentId, string name, string unit, decimal minimumQuantity, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        CreatedAt = now;
        Update(name, unit, minimumQuantity, true, now);
    }

    public bool IsLow => MinimumQuantity > 0 && QuantityOnHand <= MinimumQuantity;

    public void Update(string name, string unit, decimal minimumQuantity, bool isActive, DateTimeOffset now)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Unit = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(unit, nameof(Unit)), 10, nameof(Unit));
        MinimumQuantity = Ensure.NotNegative(minimumQuantity, nameof(MinimumQuantity));
        IsActive = isActive;
        UpdatedAt = now;
    }

    /// <summary>Adds purchased quantity and recomputes the weighted average cost.</summary>
    public void ReceivePurchase(decimal quantity, long unitCostInCents, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A quantidade da entrada deve ser maior que zero.");
        }

        Ensure.NotNegative(unitCostInCents, nameof(unitCostInCents));

        var currentQuantity = Math.Max(QuantityOnHand, 0);
        AverageCostInCents = (long)Math.Round(
            ((currentQuantity * AverageCostInCents) + (quantity * unitCostInCents)) / (currentQuantity + quantity),
            MidpointRounding.AwayFromZero);
        QuantityOnHand += quantity;
        UpdatedAt = now;
    }

    public void Adjust(decimal delta, DateTimeOffset now)
    {
        QuantityOnHand += delta;
        UpdatedAt = now;
    }
}

/// <summary>Technical sheet: how much of a stock item one unit of a menu item consumes.</summary>
public sealed class RecipeLine : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public Guid StockItemId { get; private set; }
    public decimal Quantity { get; private set; }

    private RecipeLine() { }

    public RecipeLine(Guid tenantId, Guid establishmentId, Guid menuItemId, Guid stockItemId, decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A quantidade da ficha técnica deve ser maior que zero.");
        }

        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        MenuItemId = Ensure.NotEmpty(menuItemId, nameof(MenuItemId));
        StockItemId = Ensure.NotEmpty(stockItemId, nameof(StockItemId));
        Quantity = quantity;
    }
}

public sealed class StockMovement : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid StockItemId { get; private set; }
    public StockMovementType Type { get; private set; }
    public decimal Quantity { get; private set; }
    public long UnitCostInCents { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? PurchaseEntryId { get; private set; }
    public string? Notes { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private StockMovement() { }

    public StockMovement(
        Guid tenantId,
        Guid establishmentId,
        Guid stockItemId,
        StockMovementType type,
        decimal quantity,
        long unitCostInCents,
        Guid? orderId,
        Guid? purchaseEntryId,
        string? notes,
        string? createdBy,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        StockItemId = Ensure.NotEmpty(stockItemId, nameof(StockItemId));
        Type = type;
        Quantity = quantity;
        UnitCostInCents = unitCostInCents;
        OrderId = orderId;
        PurchaseEntryId = purchaseEntryId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Ensure.MaxLength(notes.Trim(), 300, nameof(Notes));
        CreatedBy = createdBy;
        CreatedAt = now;
    }
}

public sealed class PurchaseEntry : AggregateRoot
{
    private readonly List<PurchaseEntryLine> _lines = new();

    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public string? AccessKey { get; private set; }
    public string? NfeXml { get; private set; }
    public DateOnly IssuedOn { get; private set; }
    public long TotalInCents { get; private set; }
    public string? Notes { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<PurchaseEntryLine> Lines => _lines;

    private PurchaseEntry() { }

    public PurchaseEntry(
        Guid tenantId,
        Guid establishmentId,
        Guid? supplierId,
        string? invoiceNumber,
        string? accessKey,
        DateOnly issuedOn,
        string? notes,
        string? createdBy,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        SupplierId = supplierId;
        InvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? null : Ensure.MaxLength(invoiceNumber.Trim(), 30, nameof(InvoiceNumber));
        AccessKey = string.IsNullOrWhiteSpace(accessKey) ? null : Ensure.MaxLength(accessKey.Trim(), 44, nameof(AccessKey));
        IssuedOn = issuedOn;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Ensure.MaxLength(notes.Trim(), 500, nameof(Notes));
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public void AttachNfeXml(string xml) => NfeXml = xml;

    public void AddLine(Guid stockItemId, string description, decimal quantity, long unitCostInCents)
    {
        var line = new PurchaseEntryLine(TenantId, EstablishmentId, Id, stockItemId, description, quantity, unitCostInCents);
        _lines.Add(line);
        TotalInCents = checked(TotalInCents + line.TotalInCents);
    }
}

public sealed class PurchaseEntryLine : Entity
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid PurchaseEntryId { get; private set; }
    public Guid StockItemId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public long UnitCostInCents { get; private set; }
    public long TotalInCents { get; private set; }

    private PurchaseEntryLine() { }

    internal PurchaseEntryLine(
        Guid tenantId,
        Guid establishmentId,
        Guid purchaseEntryId,
        Guid stockItemId,
        string description,
        decimal quantity,
        long unitCostInCents)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A quantidade da entrada deve ser maior que zero.");
        }

        Id = GuidV7.New();
        TenantId = tenantId;
        EstablishmentId = establishmentId;
        PurchaseEntryId = purchaseEntryId;
        StockItemId = Ensure.NotEmpty(stockItemId, nameof(StockItemId));
        Description = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(description, nameof(Description)), 200, nameof(Description));
        Quantity = quantity;
        UnitCostInCents = Ensure.NotNegative(unitCostInCents, nameof(unitCostInCents));
        TotalInCents = (long)Math.Round(quantity * unitCostInCents, MidpointRounding.AwayFromZero);
    }
}
