using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Contracts;

/// <summary>
/// Single contract per customer, kept as the aggregate root of the contract bounded context. The
/// Portal is the authority that creates, renews and expires contracts; this aggregate only applies
/// the rules that turn a contract into an access decision.
/// </summary>
public sealed class Contract : AggregateRoot
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public ContractStatus Status { get; private set; } = ContractStatus.Vigente;
    public int? MaxUsers { get; private set; }
    public int? MaxOrdersPerMonth { get; private set; }

    private readonly List<ContractModule> _modules = new();
    public IReadOnlyList<ContractModule> Modules => _modules;

    private Contract() { }

    public Contract(
        Guid tenantId,
        Guid customerId,
        Guid establishmentId,
        DateOnly startsOn,
        DateOnly? expiresOn,
        ContractStatus status,
        IReadOnlyCollection<ContractModule> modules)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        CustomerId = Ensure.NotEmpty(customerId, nameof(CustomerId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        StartsOn = startsOn;
        ExpiresOn = expiresOn;
        Status = status;
        _modules = modules?.ToList() ?? new List<ContractModule>();
    }

    /// <summary>Applies the state published by the Portal, which is the authority over the contract.</summary>
    public void ApplyPortalState(
        Guid customerId,
        Guid establishmentId,
        DateOnly startsOn,
        DateOnly? expiresOn,
        ContractStatus status,
        IReadOnlyCollection<ContractModule> modules,
        int? maxUsers,
        int? maxOrdersPerMonth)
    {
        CustomerId = Ensure.NotEmpty(customerId, nameof(CustomerId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        StartsOn = startsOn;
        ExpiresOn = expiresOn;
        Status = status;
        MaxUsers = maxUsers;
        MaxOrdersPerMonth = maxOrdersPerMonth;

        _modules.RemoveAll(current => modules.All(module => module.Code != current.Code));

        foreach (var module in modules)
        {
            var current = _modules.FirstOrDefault(existing => existing.Code == module.Code);

            if (current is null)
            {
                _modules.Add(module);
            }
            else
            {
                current.IsAvailable = module.IsAvailable;
            }
        }
    }

    /// <summary>
    /// Vigência: the contract must be Vigente and the reference date must fall inside the validity
    /// window. A contract without end date never expires on its own; renewal keeps it Vigente.
    /// </summary>
    public bool IsVigente(DateOnly today) => Status == ContractStatus.Vigente && (ExpiresOn is null || today <= ExpiresOn.Value);

    /// <summary>The operational state derived from vigência. An expired contract is blocked; the
    /// aggregate keeps the history instead of destroying anything.</summary>
    public bool IsBloqueado(DateOnly today) => !IsVigente(today);

    /// <summary>
    /// Modules published for the establishment. A module not contracted, or contracted with
    /// <see cref="ContractModule.IsAvailable"/> false, stays hidden; the product itself is not
    /// blocked by an unavailable module.
    /// </summary>
    public IReadOnlyCollection<string> LiberadosModules() => _modules
        .Where(module => module.IsAvailable)
        .Select(module => module.Code)
        .ToList();
}
