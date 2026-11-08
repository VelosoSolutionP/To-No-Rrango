using ToNoRango.Domain.Contracts;

namespace ToNoRango.Application.Contracts;

/// <summary>
/// Derived from the single contract of the client: is the access released, which modules are
/// available and why it is blocked. Kept as a value so the same decision can be audited and
/// serialized without leaking domain entities into the API layer.
/// </summary>
public enum AccessState
{
    Liberado = 1,
    Bloqueado = 2
}

public sealed record ContractAccessDecision(
    AccessState State,
    ContractStatus ContractStatus,
    DateOnly? ExpiresOn,
    IReadOnlyCollection<string> AvailableModules,
    string BlockReason)
{
    public static ContractAccessDecision Liberado(Contract contract) =>
        new(
            AccessState.Liberado,
            ContractStatus.Vigente,
            contract.ExpiresOn,
            contract.LiberadosModules(),
            string.Empty);

    public static ContractAccessDecision Bloqueado(Contract contract, DateOnly today) => new(
        AccessState.Bloqueado,
        ContractStatus.Expirado,
        contract.ExpiresOn,
        Array.Empty<string>(),
        contract.IsVigente(today) ? "Contrato indisponível no momento." : "Contrato expirado.");

    public static ContractAccessDecision SemContrato(DateOnly today) =>
        new(AccessState.Bloqueado, ContractStatus.Expirado, null, Array.Empty<string>(), "Sem contrato válido.");
}

/// <summary>
/// Turns the single contract into the access decision used by the product: released or blocked,
/// plus the modules the client may operate. The Portal remains the source of truth for the contract;
/// this service only applies the published state to the tenant.
/// </summary>
public sealed class ContractAccessService
{
    public ContractAccessDecision Evaluate(Contract? contract, DateOnly today)
    {
        if (contract is null)
        {
            return ContractAccessDecision.SemContrato(today);
        }

        return contract.IsVigente(today)
            ? ContractAccessDecision.Liberado(contract)
            : ContractAccessDecision.Bloqueado(contract, today);
    }
}
