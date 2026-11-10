using Microsoft.EntityFrameworkCore;
using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Contracts;
using ToNoRango.Domain.Delivery;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Identity;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Orders;
using ToNoRango.Domain.Parties;
using ToNoRango.Domain.Payments;
using ToNoRango.Domain.Zest;

namespace ToNoRango.Infrastructure.Persistence;

public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;
    private bool _suppressed;

    public Guid? CurrentTenantId => _suppressed ? null : _tenantId;

    public void SetTenant(Guid? tenantId) => _tenantId = tenantId;

    public IDisposable SuppressTenantFilter()
    {
        _suppressed = true;
        return new SuppressionScope(this);
    }

    private sealed class SuppressionScope(TenantContext owner) : IDisposable
    {
        public void Dispose() => owner._suppressed = false;
    }
}

public interface ITenantContext
{
    Guid? CurrentTenantId { get; }

    void SetTenant(Guid? tenantId);

    IDisposable SuppressTenantFilter();
}

public sealed class ToNoRangoDbContext : DbContext
{
    public ToNoRangoDbContext(DbContextOptions<ToNoRangoDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        TenantContext = tenantContext;
    }

    public ITenantContext TenantContext { get; }

    public DbSet<FiscalProfile> FiscalProfiles => Set<FiscalProfile>();
    public DbSet<ProductFiscalData> ProductFiscalData => Set<ProductFiscalData>();
    public DbSet<FiscalDocument> FiscalDocuments => Set<FiscalDocument>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Establishment> Establishments => Set<Establishment>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuOptionGroup> MenuOptionGroups => Set<MenuOptionGroup>();
    public DbSet<MenuOption> MenuOptions => Set<MenuOption>();
    public DbSet<MenuCombo> MenuCombos => Set<MenuCombo>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemOption> OrderItemOptions => Set<OrderItemOption>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ToNoRangoDbContext).Assembly);

        ApplyTenantFilter<FiscalProfile>(modelBuilder);
        ApplyTenantFilter<ProductFiscalData>(modelBuilder);
        ApplyTenantFilter<FiscalDocument>(modelBuilder);
        ApplyTenantFilter<Contract>(modelBuilder);
        ApplyTenantFilter<Establishment>(modelBuilder);
        ApplyTenantFilter<MenuCategory>(modelBuilder);
        ApplyTenantFilter<MenuItem>(modelBuilder);
        ApplyTenantFilter<MenuOptionGroup>(modelBuilder);
        ApplyTenantFilter<MenuOption>(modelBuilder);
        ApplyTenantFilter<MenuCombo>(modelBuilder);
        ApplyTenantFilter<MenuComboLine>(modelBuilder);
        ApplyTenantFilter<Order>(modelBuilder);
        ApplyTenantFilter<OrderItem>(modelBuilder);
        ApplyTenantFilter<OrderItemOption>(modelBuilder);
        ApplyTenantFilter<OrderStatusHistory>(modelBuilder);
        ApplyTenantFilter<User>(modelBuilder);
        ApplyTenantFilter<Payment>(modelBuilder);
        ApplyTenantFilter<PaymentSettings>(modelBuilder);
        ApplyTenantFilter<Customer>(modelBuilder);
        ApplyTenantFilter<Supplier>(modelBuilder);
        ApplyTenantFilter<StockItem>(modelBuilder);
        ApplyTenantFilter<RecipeLine>(modelBuilder);
        ApplyTenantFilter<StockMovement>(modelBuilder);
        ApplyTenantFilter<PurchaseEntry>(modelBuilder);
        ApplyTenantFilter<PurchaseEntryLine>(modelBuilder);
        ApplyTenantFilter<FinancialEntry>(modelBuilder);
        ApplyTenantFilter<DeliveryZone>(modelBuilder);
        ApplyTenantFilter<Courier>(modelBuilder);
        ApplyTenantFilter<WhatsAppSettings>(modelBuilder);
        ApplyTenantFilter<OutboundMessage>(modelBuilder);
        ApplyTenantFilter<Conversation>(modelBuilder);
        ApplyTenantFilter<ConversationMessage>(modelBuilder);
        ApplyTenantFilter<KnowledgeEntry>(modelBuilder);
        ApplyTenantFilter<ToNoRango.Domain.Pdv.CashSession>(modelBuilder);
        ApplyTenantFilter<ToNoRango.Domain.Pdv.CashMovement>(modelBuilder);
        ApplyTenantFilter<ToNoRango.Domain.Feedback.OrderRating>(modelBuilder);
        ApplyTenantFilter<KnowledgeGap>(modelBuilder);
        ApplyTenantFilter<FiscalNumberGap>(modelBuilder);
        ApplyTenantFilter<ToNoRango.Domain.Media.MediaFile>(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 8);
        configurationBuilder.Properties<string>().HaveMaxLength(512);

        base.ConfigureConventions(configurationBuilder);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
            TenantContext.CurrentTenantId == null
            || EF.Property<Guid>(entity, nameof(Contract.TenantId)) == TenantContext.CurrentTenantId);
    }
}
