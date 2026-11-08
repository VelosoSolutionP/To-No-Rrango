using ToNoRango.Domain.Common;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Parties;

public sealed class Customer : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string PhoneKey { get; private set; } = string.Empty;
    public string? Document { get; private set; }
    public string? Email { get; private set; }
    public string? Notes { get; private set; }
    public int OrdersCount { get; private set; }
    public long TotalOrderedInCents { get; private set; }
    public DateTimeOffset? LastOrderAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Customer() { }

    public Customer(Guid tenantId, Guid establishmentId, string name, string phone, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Phone = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(phone, nameof(Phone)), 30, nameof(Phone));
        PhoneKey = PhoneNumber.Key(phone);
        CreatedAt = now;
        Update(name, null, null, null, now);
    }

    public void Update(string name, string? document, string? email, string? notes, DateTimeOffset now)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Document = PartyRules.NormalizeDocument(document);
        Email = PartyRules.Optional(email, 200, nameof(Email));
        Notes = PartyRules.Optional(notes, 500, nameof(Notes));
        UpdatedAt = now;
    }
}

public sealed class Supplier : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public string? Document { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Supplier() { }

    public Supplier(
        Guid tenantId,
        Guid establishmentId,
        string name,
        string? tradeName,
        string? document,
        string? phone,
        string? email,
        string? address,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        CreatedAt = now;
        Update(name, tradeName, document, phone, email, address, true, now);
    }

    public void Update(
        string name,
        string? tradeName,
        string? document,
        string? phone,
        string? email,
        string? address,
        bool isActive,
        DateTimeOffset now)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 160, nameof(Name));
        TradeName = PartyRules.Optional(tradeName, 160, nameof(TradeName));
        Document = PartyRules.NormalizeDocument(document);
        Phone = PartyRules.Optional(phone, 30, nameof(Phone));
        Email = PartyRules.Optional(email, 200, nameof(Email));
        Address = PartyRules.Optional(address, 300, nameof(Address));
        IsActive = isActive;
        UpdatedAt = now;
    }
}

internal static class PartyRules
{
    public static string? Optional(string? value, int maxLength, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null : Ensure.MaxLength(value.Trim(), maxLength, paramName);

    public static string? NormalizeDocument(string? document)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return null;
        }

        if (!DocumentValidator.IsValid(document))
        {
            throw new ArgumentException("CPF ou CNPJ inválido.", nameof(document));
        }

        return PhoneNumber.Digits(document);
    }
}
