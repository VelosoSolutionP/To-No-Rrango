using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Catalog;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items", "catalog");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.EstablishmentId).IsRequired();
        builder.Property(item => item.CategoryId).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(500);
        builder.Property(item => item.ImageUrl).HasMaxLength(500);
        builder.Property(item => item.PriceInCents).IsRequired();
        builder.Property(item => item.SortOrder).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => new { item.EstablishmentId, item.CategoryId, item.SortOrder });

        builder
            .HasMany(item => item.OptionGroups)
            .WithOne()
            .HasForeignKey(group => group.MenuItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(item => item.OptionGroups).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
