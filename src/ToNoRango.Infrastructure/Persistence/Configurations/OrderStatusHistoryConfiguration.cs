using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("order_status_history", "orders");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Id).ValueGeneratedNever();
        builder.Property(history => history.TenantId).IsRequired();
        builder.Property(history => history.EstablishmentId).IsRequired();
        builder.Property(history => history.OrderId).IsRequired();
        builder.Property(history => history.FromStatus).HasConversion<int>();
        builder.Property(history => history.ToStatus).HasConversion<int>().IsRequired();
        builder.Property(history => history.ChangedAt).IsRequired();
        builder.Property(history => history.ChangedBy).HasMaxLength(120);
        builder.Property(history => history.Reason).HasMaxLength(300);

        builder.HasIndex(history => history.TenantId);
        builder.HasIndex(history => new { history.OrderId, history.ChangedAt });
    }
}
