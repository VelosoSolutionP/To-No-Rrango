using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Contracts;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts", "contracts");
        builder.HasKey(contract => contract.Id);
        builder.Property(contract => contract.Id).ValueGeneratedNever();
        builder.Property(contract => contract.TenantId).IsRequired();
        builder.Property(contract => contract.CustomerId).IsRequired();
        builder.Property(contract => contract.EstablishmentId).IsRequired();
        builder.Property(contract => contract.StartsOn).IsRequired();
        builder.Property(contract => contract.ExpiresOn);
        builder.Property(contract => contract.Status).HasConversion<int>().IsRequired();

        builder.HasIndex(contract => contract.TenantId).IsUnique();
        builder.HasIndex(contract => contract.EstablishmentId);

        builder.OwnsMany(contract => contract.Modules, modules =>
        {
            modules.ToTable("contract_modules", "contracts");
            modules.WithOwner().HasForeignKey("ContractId");
            modules.Property(module => module.Code).HasColumnName("code").HasMaxLength(80).IsRequired();
            modules.Property(module => module.IsAvailable).HasColumnName("is_available").IsRequired();
            modules.HasKey("ContractId", nameof(ContractModule.Code));
        });

        builder.Navigation(contract => contract.Modules).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
