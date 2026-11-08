using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Catalog;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class MenuOptionConfiguration : IEntityTypeConfiguration<MenuOption>
{
    public void Configure(EntityTypeBuilder<MenuOption> builder)
    {
        builder.ToTable("menu_options", "catalog");
        builder.HasKey(option => option.Id);
        builder.Property(option => option.Id).ValueGeneratedNever();
        builder.Property(option => option.TenantId).IsRequired();
        builder.Property(option => option.EstablishmentId).IsRequired();
        builder.Property(option => option.MenuOptionGroupId).IsRequired();
        builder.Property(option => option.Name).HasMaxLength(80).IsRequired();
        builder.Property(option => option.PriceInCents).IsRequired();
        builder.Property(option => option.SortOrder).IsRequired();

        builder.HasIndex(option => option.TenantId);
        builder.HasIndex(option => new { option.MenuOptionGroupId, option.SortOrder });
    }
}
