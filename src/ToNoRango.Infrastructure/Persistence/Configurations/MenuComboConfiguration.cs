using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Catalog;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class MenuComboConfiguration : IEntityTypeConfiguration<MenuCombo>
{
    public void Configure(EntityTypeBuilder<MenuCombo> builder)
    {
        builder.ToTable("menu_combos", "catalog");
        builder.HasKey(combo => combo.Id);
        builder.Property(combo => combo.Id).ValueGeneratedNever();
        builder.Property(combo => combo.TenantId).IsRequired();
        builder.Property(combo => combo.EstablishmentId).IsRequired();
        builder.Property(combo => combo.MenuItemId).IsRequired();

        builder.HasIndex(combo => combo.MenuItemId).IsUnique();
        builder.HasIndex(combo => combo.TenantId);

        builder
            .HasMany(combo => combo.Lines)
            .WithOne()
            .HasForeignKey(line => line.MenuComboId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(combo => combo.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class MenuComboLineConfiguration : IEntityTypeConfiguration<MenuComboLine>
{
    public void Configure(EntityTypeBuilder<MenuComboLine> builder)
    {
        builder.ToTable("menu_combo_lines", "catalog");
        builder.HasKey(line => new { line.MenuComboId, line.ConstituentMenuItemId });
        builder.Property(line => line.TenantId).IsRequired();
        builder.Property(line => line.EstablishmentId).IsRequired();
        builder.Property(line => line.MenuComboId).IsRequired();
        builder.Property(line => line.ConstituentMenuItemId).IsRequired();
        builder.Property(line => line.Quantity).IsRequired();

        builder.HasIndex(line => line.TenantId);
    }
}
