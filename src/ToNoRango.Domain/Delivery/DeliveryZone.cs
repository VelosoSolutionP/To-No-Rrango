using ToNoRango.Domain.Establishments;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Delivery;

public sealed class DeliveryZone : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string District { get; private set; } = string.Empty;
    public string DistrictKey { get; private set; } = string.Empty;
    public long FeeInCents { get; private set; }
    public int? EstimatedMinutes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private DeliveryZone() { }

    public DeliveryZone(Guid tenantId, Guid establishmentId, string district, long feeInCents, int? estimatedMinutes)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Update(district, feeInCents, estimatedMinutes, true);
    }

    public static string KeyOf(string district) => Establishment.NormalizeSlug(district);

    public void Update(string district, long feeInCents, int? estimatedMinutes, bool isActive)
    {
        District = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(district, nameof(District)), 80, nameof(District));
        DistrictKey = KeyOf(district);
        FeeInCents = Ensure.NotNegative(feeInCents, nameof(FeeInCents));
        EstimatedMinutes = estimatedMinutes;
        IsActive = isActive;
    }
}
