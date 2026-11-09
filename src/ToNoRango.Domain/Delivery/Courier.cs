using ToNoRango.Domain.Common;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Delivery;

public sealed class Courier : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private Courier() { }

    public Courier(Guid tenantId, Guid establishmentId, string name, string phone)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Update(name, phone, true);
    }

    /// <summary>Returns the number in the international form used by WhatsApp links.</summary>
    public static string NormalizePhone(string? phone)
    {
        var number = PhoneNumber.ToWhatsApp(phone);

        return number.Length is 12 or 13 && number.StartsWith("55", StringComparison.Ordinal)
            ? number
            : throw new ArgumentException("Informe o WhatsApp com DDD.", nameof(phone));
    }

    public void Update(string name, string phone, bool isActive)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)).Trim(), 120, nameof(Name));
        Phone = NormalizePhone(phone);
        IsActive = isActive;
    }
}
