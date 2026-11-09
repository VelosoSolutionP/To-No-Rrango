using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Parties;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Inventory;

public sealed record StockItemRequest(string Name, string Unit, decimal MinimumQuantity, bool IsActive = true);

public sealed record StockItemDto(
    Guid Id,
    string Name,
    string Unit,
    decimal QuantityOnHand,
    decimal MinimumQuantity,
    long AverageCostInCents,
    bool IsActive,
    bool IsLow)
{
    public static StockItemDto From(StockItem item) => new(
        item.Id,
        item.Name,
        item.Unit,
        item.QuantityOnHand,
        item.MinimumQuantity,
        item.AverageCostInCents,
        item.IsActive,
        item.IsLow);
}

public sealed record StockAdjustmentRequest(StockMovementType Type, decimal Quantity, string? Notes);

public sealed record StockMovementDto(
    Guid Id,
    Guid StockItemId,
    StockMovementType Type,
    decimal Quantity,
    long UnitCostInCents,
    string? Notes,
    string? CreatedBy,
    DateTimeOffset CreatedAt)
{
    public static StockMovementDto From(StockMovement movement) => new(
        movement.Id,
        movement.StockItemId,
        movement.Type,
        movement.Quantity,
        movement.UnitCostInCents,
        movement.Notes,
        movement.CreatedBy,
        movement.CreatedAt);
}

public sealed record RecipeLineRequest(Guid StockItemId, decimal Quantity);

public sealed record RecipeLineDto(Guid StockItemId, decimal Quantity);

public sealed record NewSupplierRequest(string Name, string? TradeName, string? Document);

public sealed record PurchaseLineRequest(Guid StockItemId, string Description, decimal Quantity, long UnitCostInCents);

public sealed record CreatePurchaseRequest(
    Guid? SupplierId,
    NewSupplierRequest? NewSupplier,
    string? InvoiceNumber,
    string? AccessKey,
    DateOnly IssuedOn,
    DateOnly? DueOn,
    string? Notes,
    IReadOnlyList<PurchaseLineRequest> Lines,
    string? NfeXml = null);

public sealed record PurchaseLineDto(Guid StockItemId, string Description, decimal Quantity, long UnitCostInCents, long TotalInCents);

public sealed record PurchaseDto(
    Guid Id,
    Guid? SupplierId,
    string? InvoiceNumber,
    string? AccessKey,
    DateOnly IssuedOn,
    long TotalInCents,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PurchaseLineDto> Lines)
{
    public static PurchaseDto From(PurchaseEntry entry) => new(
        entry.Id,
        entry.SupplierId,
        entry.InvoiceNumber,
        entry.AccessKey,
        entry.IssuedOn,
        entry.TotalInCents,
        entry.CreatedAt,
        entry.Lines.Select(line => new PurchaseLineDto(line.StockItemId, line.Description, line.Quantity, line.UnitCostInCents, line.TotalInCents)).ToList());
}

public sealed class InventoryService(
    IRepository<StockItem> stockItems,
    IRepository<StockMovement> movements,
    IRepository<RecipeLine> recipes,
    IRepository<PurchaseEntry> purchases,
    IRepository<Supplier> suppliers,
    IRepository<FinancialEntry> entries,
    IMenuRepository menu,
    IEstablishmentRepository establishments,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly Error StockItemNotFound = Error.NotFound("stock.not_found", "Insumo não encontrado.");

    public async Task<Result<IReadOnlyList<StockItemDto>>> ListItemsAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await stockItems.ListAsync(item => item.EstablishmentId == establishmentId, item => item.Name, cancellationToken);
        return list.Select(StockItemDto.From).ToList();
    }

    public Task<Result<StockItemDto>> CreateItemAsync(Guid establishmentId, StockItemRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<StockItemDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<StockItemDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var item = new StockItem(establishment.TenantId, establishment.Id, request.Name, request.Unit, request.MinimumQuantity, clock.UtcNow);
            stockItems.Add(item);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return StockItemDto.From(item);
        });

    public Task<Result<StockItemDto>> UpdateItemAsync(Guid itemId, StockItemRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<StockItemDto>(async () =>
        {
            var item = await stockItems.FindAsync(itemId, cancellationToken);

            if (item is null)
            {
                return Result<StockItemDto>.Failure(StockItemNotFound);
            }

            item.Update(request.Name, request.Unit, request.MinimumQuantity, request.IsActive, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return StockItemDto.From(item);
        });

    public Task<Result<StockItemDto>> AdjustAsync(Guid itemId, StockAdjustmentRequest request, string? createdBy, CancellationToken cancellationToken) =>
        Guarded.RunAsync<StockItemDto>(async () =>
        {
            if (request.Type is not (StockMovementType.Adjustment or StockMovementType.Loss))
            {
                return Result<StockItemDto>.Failure(AppErrors.Validation("Use ajuste ou perda neste lançamento."));
            }

            if (request.Quantity == 0 || (request.Type == StockMovementType.Loss && request.Quantity > 0))
            {
                return Result<StockItemDto>.Failure(AppErrors.Validation("Informe uma quantidade válida. Perdas devem ser negativas."));
            }

            var item = await stockItems.FindAsync(itemId, cancellationToken);

            if (item is null)
            {
                return Result<StockItemDto>.Failure(StockItemNotFound);
            }

            item.Adjust(request.Quantity, clock.UtcNow);
            movements.Add(new StockMovement(
                item.TenantId,
                item.EstablishmentId,
                item.Id,
                request.Type,
                request.Quantity,
                item.AverageCostInCents,
                null,
                null,
                request.Notes,
                createdBy,
                clock.UtcNow));

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return StockItemDto.From(item);
        });

    public async Task<Result<IReadOnlyList<StockMovementDto>>> ListMovementsAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await movements.ListAsync(
            movement => movement.EstablishmentId == establishmentId,
            movement => movement.CreatedAt,
            cancellationToken,
            descending: true,
            take: 200);

        return list.Select(StockMovementDto.From).ToList();
    }

    public async Task<Result<IReadOnlyList<RecipeLineDto>>> GetRecipeAsync(Guid menuItemId, CancellationToken cancellationToken)
    {
        var lines = await recipes.ListAsync(line => line.MenuItemId == menuItemId, line => line.Id, cancellationToken);
        return lines.Select(line => new RecipeLineDto(line.StockItemId, line.Quantity)).ToList();
    }

    public Task<Result<IReadOnlyList<RecipeLineDto>>> SetRecipeAsync(
        Guid menuItemId,
        IReadOnlyList<RecipeLineRequest> request,
        CancellationToken cancellationToken) =>
        Guarded.RunAsync<IReadOnlyList<RecipeLineDto>>(async () =>
        {
            var item = await menu.GetItemAsync(menuItemId, cancellationToken);

            if (item is null)
            {
                return Result<IReadOnlyList<RecipeLineDto>>.Failure(AppErrors.NotFound("item.not_found", "Item não encontrado."));
            }

            if (request.Select(line => line.StockItemId).Distinct().Count() != request.Count)
            {
                return Result<IReadOnlyList<RecipeLineDto>>.Failure(AppErrors.Validation("Um insumo aparece mais de uma vez na ficha técnica."));
            }

            var stockIds = request.Select(line => line.StockItemId).ToList();
            var known = await stockItems.CountAsync(
                stock => stockIds.Contains(stock.Id) && stock.EstablishmentId == item.EstablishmentId,
                cancellationToken);

            if (known != stockIds.Count)
            {
                return Result<IReadOnlyList<RecipeLineDto>>.Failure(AppErrors.Validation("Um dos insumos não pertence a este estabelecimento."));
            }

            var current = await recipes.ListAsync(line => line.MenuItemId == menuItemId, line => line.Id, cancellationToken);

            foreach (var line in current)
            {
                recipes.Remove(line);
            }

            var created = request
                .Select(line => new RecipeLine(item.TenantId, item.EstablishmentId, item.Id, line.StockItemId, line.Quantity))
                .ToList();

            foreach (var line in created)
            {
                recipes.Add(line);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return created.Select(line => new RecipeLineDto(line.StockItemId, line.Quantity)).ToList();
        });

    public async Task<Result<IReadOnlyList<PurchaseDto>>> ListPurchasesAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await purchases.ListAsync(
            purchase => purchase.EstablishmentId == establishmentId,
            purchase => purchase.CreatedAt,
            cancellationToken,
            descending: true,
            take: 100);

        return list.Select(PurchaseDto.From).ToList();
    }

    public Task<Result<PurchaseDto>> CreatePurchaseAsync(
        Guid establishmentId,
        CreatePurchaseRequest request,
        string? createdBy,
        CancellationToken cancellationToken) =>
        Guarded.RunAsync<PurchaseDto>(() => unitOfWork.ExecuteInTransactionAsync<Result<PurchaseDto>>(async token =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, token);

            if (establishment is null)
            {
                return Result<PurchaseDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            if (request.Lines is null || request.Lines.Count == 0)
            {
                return Result<PurchaseDto>.Failure(AppErrors.Validation("A entrada precisa ter pelo menos um item."));
            }

            if (!string.IsNullOrWhiteSpace(request.AccessKey)
                && await purchases.AnyAsync(item => item.EstablishmentId == establishment.Id && item.AccessKey == request.AccessKey, token))
            {
                return Result<PurchaseDto>.Failure(AppErrors.Conflict("purchase.duplicate", "Esta nota fiscal já foi lançada."));
            }

            // Everything is validated before the first change: a failed result still commits the unit of work.
            var loaded = new List<(PurchaseLineRequest Line, StockItem Item)>();

            foreach (var line in request.Lines)
            {
                var stockItem = await stockItems.FindAsync(line.StockItemId, token);

                if (stockItem is null || stockItem.EstablishmentId != establishment.Id)
                {
                    return Result<PurchaseDto>.Failure(StockItemNotFound);
                }

                loaded.Add((line, stockItem));
            }

            var now = clock.UtcNow;
            var supplierId = await ResolveSupplierAsync(establishment.TenantId, establishment.Id, request, now, token);

            if (supplierId.IsFailure)
            {
                return Result<PurchaseDto>.Failure(supplierId.Error);
            }

            var entry = new PurchaseEntry(
                establishment.TenantId,
                establishment.Id,
                supplierId.Value,
                request.InvoiceNumber,
                request.AccessKey,
                request.IssuedOn,
                request.Notes,
                createdBy,
                now);

            if (!string.IsNullOrWhiteSpace(request.NfeXml))
            {
                var parsedNote = ToNoRango.Application.Fiscal.NfeXmlParser.Parse(request.NfeXml);

                if (parsedNote.AccessKey != request.AccessKey)
                {
                    return Result<PurchaseDto>.Failure(AppErrors.Validation("O XML enviado não é o desta compra."));
                }

                entry.AttachNfeXml(request.NfeXml);
            }

            foreach (var (line, stockItem) in loaded)
            {
                entry.AddLine(stockItem.Id, line.Description, line.Quantity, line.UnitCostInCents);
                stockItem.ReceivePurchase(line.Quantity, line.UnitCostInCents, now);
                movements.Add(new StockMovement(
                    establishment.TenantId,
                    establishment.Id,
                    stockItem.Id,
                    StockMovementType.Purchase,
                    line.Quantity,
                    line.UnitCostInCents,
                    null,
                    entry.Id,
                    string.IsNullOrWhiteSpace(request.InvoiceNumber) ? "Entrada de mercadoria" : $"NF {request.InvoiceNumber}",
                    createdBy,
                    now));
            }

            purchases.Add(entry);

            if (entry.TotalInCents > 0)
            {
                entries.Add(new FinancialEntry(
                    establishment.TenantId,
                    establishment.Id,
                    FinancialType.Payable,
                    FinancialSource.Purchase,
                    entry.Id,
                    "Compras",
                    string.IsNullOrWhiteSpace(request.InvoiceNumber) ? "Entrada de mercadoria" : $"NF {request.InvoiceNumber}",
                    entry.TotalInCents,
                    request.DueOn ?? request.IssuedOn,
                    now));
            }

            return PurchaseDto.From(entry);
        }, cancellationToken));

    private async Task<Result<Guid?>> ResolveSupplierAsync(
        Guid tenantId,
        Guid establishmentId,
        CreatePurchaseRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.SupplierId is { } id)
        {
            var known = await suppliers.AnyAsync(item => item.Id == id && item.EstablishmentId == establishmentId, cancellationToken);

            return known
                ? Result<Guid?>.Success(id)
                : Result<Guid?>.Failure(AppErrors.NotFound("supplier.not_found", "Fornecedor não encontrado."));
        }

        if (request.NewSupplier is null)
        {
            return Result<Guid?>.Success(null);
        }

        var supplier = new Supplier(
            tenantId,
            establishmentId,
            request.NewSupplier.Name,
            request.NewSupplier.TradeName,
            request.NewSupplier.Document,
            null,
            null,
            null,
            now);

        var existing = supplier.Document is null
            ? null
            : await suppliers.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId && item.Document == supplier.Document, cancellationToken);

        if (existing is not null)
        {
            return Result<Guid?>.Success(existing.Id);
        }

        suppliers.Add(supplier);
        return Result<Guid?>.Success(supplier.Id);
    }
}
