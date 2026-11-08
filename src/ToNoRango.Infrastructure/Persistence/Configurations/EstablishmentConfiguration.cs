using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Establishments;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class EstablishmentConfiguration : IEntityTypeConfiguration<Establishment>
{
    public void Configure(EntityTypeBuilder<Establishment> builder)
    {
        builder.ToTable("establishments", "establishments");
        builder.HasKey(establishment => establishment.Id);
        builder.Property(establishment => establishment.Id).ValueGeneratedNever();
        builder.Property(establishment => establishment.TenantId).IsRequired();
        builder.Property(establishment => establishment.Name).HasMaxLength(120).IsRequired();
        builder.Property(establishment => establishment.Slug).HasMaxLength(80).IsRequired();
        builder.Property(establishment => establishment.Description).HasMaxLength(500);
        builder.Property(establishment => establishment.LogoUrl).HasMaxLength(500);
        builder.Property(establishment => establishment.CoverUrl).HasMaxLength(500);
        builder.Property(establishment => establishment.Phone).HasMaxLength(30);
        builder.Property(establishment => establishment.WhatsApp).HasMaxLength(30);
        builder.Property(establishment => establishment.AddressLine).HasMaxLength(200);
        builder.Property(establishment => establishment.City).HasMaxLength(80);
        builder.Property(establishment => establishment.State).HasMaxLength(2);
        builder.Property(establishment => establishment.PostalCode).HasMaxLength(10);
        builder.Property(establishment => establishment.TimeZone).HasMaxLength(80).IsRequired();
        builder.Property(establishment => establishment.PrimaryColor).HasMaxLength(7).IsRequired();
        builder.Property(establishment => establishment.SecondaryColor).HasMaxLength(7).IsRequired();
        builder.Property(establishment => establishment.DeliveryFeeInCents).IsRequired();
        builder.Property(establishment => establishment.MinimumOrderInCents).IsRequired();
        builder.Property(establishment => establishment.CreatedAt).IsRequired();
        builder.Property(establishment => establishment.UpdatedAt).IsRequired();

        builder.HasIndex(establishment => establishment.Slug).IsUnique();
        builder.HasIndex(establishment => establishment.TenantId);
    }
}
