using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", "orders");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.EstablishmentId).IsRequired();
        builder.Property(item => item.OrderId).IsRequired();
        builder.Property(item => item.MenuItemId).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(120).IsRequired();
        builder.Property(item => item.UnitPriceInCents).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.Notes).HasMaxLength(500);
        builder.Property(item => item.TotalInCents).IsRequired();

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => item.OrderId);

        builder
            .HasMany(item => item.Options)
            .WithOne()
            .HasForeignKey(option => option.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(item => item.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
