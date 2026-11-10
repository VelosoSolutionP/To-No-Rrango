using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Catalog;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("menu_categories", "catalog");
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.TenantId).IsRequired();
        builder.Property(category => category.EstablishmentId).IsRequired();
        builder.Property(category => category.Name).HasMaxLength(80).IsRequired();
        builder.Property(category => category.Description).HasMaxLength(300);
        builder.Property(category => category.SortOrder).IsRequired();
        builder.Property(category => category.CreatedAt).IsRequired();
        builder.Property(category => category.Station).HasConversion<int>().HasDefaultValue(ToNoRango.Domain.Orders.PrepStation.Kitchen);
        builder.Property(category => category.UpdatedAt).IsRequired();

        builder.HasIndex(category => category.TenantId);
        builder.HasIndex(category => new { category.EstablishmentId, category.SortOrder });
    }
}
