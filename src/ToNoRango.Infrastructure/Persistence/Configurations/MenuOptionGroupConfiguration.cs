using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Catalog;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class MenuOptionGroupConfiguration : IEntityTypeConfiguration<MenuOptionGroup>
{
    public void Configure(EntityTypeBuilder<MenuOptionGroup> builder)
    {
        builder.ToTable("menu_option_groups", "catalog");
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();
        builder.Property(group => group.TenantId).IsRequired();
        builder.Property(group => group.EstablishmentId).IsRequired();
        builder.Property(group => group.MenuItemId).IsRequired();
        builder.Property(group => group.Name).HasMaxLength(80).IsRequired();
        builder.Property(group => group.MinSelections).IsRequired();
        builder.Property(group => group.MaxSelections).IsRequired();
        builder.Property(group => group.SortOrder).IsRequired();

        builder.HasIndex(group => group.TenantId);
        builder.HasIndex(group => new { group.MenuItemId, group.SortOrder });

        builder
            .HasMany(group => group.Options)
            .WithOne()
            .HasForeignKey(option => option.MenuOptionGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(group => group.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
