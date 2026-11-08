using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.TenantId).IsRequired();
        builder.Property(order => order.EstablishmentId).IsRequired();
        builder.Property(order => order.Code).HasMaxLength(20).IsRequired();
        builder.Property(order => order.CustomerName).HasMaxLength(120).IsRequired();
        builder.Property(order => order.CustomerPhone).HasMaxLength(30).IsRequired();
        builder.Property(order => order.CustomerPhoneKey).HasMaxLength(20).IsRequired();
        builder.Property(order => order.Channel).HasConversion<int>().HasDefaultValue(OrderChannel.Web);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(order => order.FulfillmentType).HasConversion<int>().IsRequired();
        builder.Property(order => order.Status).HasConversion<int>().IsRequired();
        builder.Property(order => order.PaymentMethod).HasConversion<int>().IsRequired();
        builder.Property(order => order.PaymentStatus).HasConversion<int>().IsRequired();
        builder.Property(order => order.Notes).HasMaxLength(500);
        builder.Property(order => order.DeliveryStreet).HasMaxLength(200);
        builder.Property(order => order.DeliveryNumber).HasMaxLength(20);
        builder.Property(order => order.DeliveryComplement).HasMaxLength(100);
        builder.Property(order => order.DeliveryDistrict).HasMaxLength(80);
        builder.Property(order => order.DeliveryCity).HasMaxLength(80);
        builder.Property(order => order.DeliveryState).HasMaxLength(2);
        builder.Property(order => order.DeliveryPostalCode).HasMaxLength(10);
        builder.Property(order => order.SubtotalInCents).IsRequired();
        builder.Property(order => order.DeliveryFeeInCents).IsRequired();
        builder.Property(order => order.TotalInCents).IsRequired();
        builder.Property(order => order.IdempotencyKey).HasMaxLength(100);
        builder.Property(order => order.CancelReason).HasMaxLength(300);
        builder.Property(order => order.CancelledBy).HasMaxLength(120);
        builder.Property(order => order.CreatedAt).IsRequired();
        builder.Property(order => order.UpdatedAt).IsRequired();

        builder.HasIndex(order => order.Code).IsUnique();
        builder.HasIndex(order => order.TenantId);
        builder.HasIndex(order => new { order.EstablishmentId, order.CustomerPhoneKey });
        builder.HasIndex(order => new { order.EstablishmentId, order.CreatedAt });
        builder.HasIndex(order => new { order.EstablishmentId, order.IdempotencyKey }).IsUnique();

        builder
            .HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(order => order.StatusHistory)
            .WithOne()
            .HasForeignKey(history => history.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(order => order.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
