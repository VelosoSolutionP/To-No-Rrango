using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class OrderItemOptionConfiguration : IEntityTypeConfiguration<OrderItemOption>
{
    public void Configure(EntityTypeBuilder<OrderItemOption> builder)
    {
        builder.ToTable("order_item_options", "orders");
        builder.HasKey(option => option.Id);
        builder.Property(option => option.Id).ValueGeneratedNever();
        builder.Property(option => option.TenantId).IsRequired();
        builder.Property(option => option.EstablishmentId).IsRequired();
        builder.Property(option => option.OrderItemId).IsRequired();
        builder.Property(option => option.GroupName).HasMaxLength(80).IsRequired();
        builder.Property(option => option.Name).HasMaxLength(80).IsRequired();
        builder.Property(option => option.PriceInCents).IsRequired();

        builder.HasIndex(option => option.TenantId);
        builder.HasIndex(option => option.OrderItemId);
    }
}
