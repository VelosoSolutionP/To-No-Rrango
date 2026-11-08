using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Contracts;
using ToNoRango.Domain.Identity;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Contracts;

public static class Modules
{
    public const string Cardapio = "cardapio";
    public const string Pedidos = "pedidos";
    public const string Pagamentos = "pagamentos";
    public const string WhatsApp = "whatsapp";
    public const string Clientes = "clientes";
    public const string Estoque = "estoque";
    public const string Financeiro = "financeiro";
    public const string Fiscal = "fiscal";
    public const string Relatorios = "relatorios";
    public const string Ia = "ia";

    /// <summary>Paid add-on: lets ZEST fall back to an LLM when its own rules have no answer.</summary>
    public const string IaLlm = "ia_llm";
}

public sealed record PublishContractRequest(
    Guid CustomerId,
    Guid EstablishmentId,
    DateOnly StartsOn,
    DateOnly? ExpiresOn,
    ContractStatus Status,
    IReadOnlyList<ContractModuleRequest> Modules,
    int? MaxUsers,
    int? MaxOrdersPerMonth);

public sealed record ContractModuleRequest(string Code, bool IsAvailable = true);

public sealed record ContractStateDto(
    AccessState State,
    DateOnly? ExpiresOn,
    IReadOnlyCollection<string> Modules,
    string BlockReason,
    int? MaxUsers,
    int? MaxOrdersPerMonth);

public sealed class ContractService(
    IRepository<Contract> contracts,
    IRepository<User> users,
    IRepository<Order> orders,
    ContractAccessService access,
    IUnitOfWork unitOfWork,
    IClock clock) : IModuleAccess
{
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    public async Task<bool> IsAllowedAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(tenantId, cancellationToken);
        return state.State == AccessState.Liberado && state.Modules.Contains(moduleCode);
    }

    public async Task<ContractStateDto> GetStateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var contract = await contracts.FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);
        var decision = access.Evaluate(contract, Today());

        return new ContractStateDto(
            decision.State,
            decision.ExpiresOn,
            decision.AvailableModules,
            decision.BlockReason,
            contract?.MaxUsers,
            contract?.MaxOrdersPerMonth);
    }

    public async Task<Result<ContractStateDto>> PublishAsync(
        Guid tenantId,
        PublishContractRequest request,
        CancellationToken cancellationToken) => await Guarded.RunAsync<ContractStateDto>(async () =>
    {
        var modules = (request.Modules ?? [])
            .Where(module => !string.IsNullOrWhiteSpace(module.Code))
            .GroupBy(module => module.Code.Trim().ToLowerInvariant())
            .Select(group => new ContractModule(group.Key, group.Last().IsAvailable))
            .ToList();

        var contract = await contracts.FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        if (contract is null)
        {
            contract = new Contract(tenantId, request.CustomerId, request.EstablishmentId, request.StartsOn, request.ExpiresOn, request.Status, modules);
            contract.ApplyPortalState(request.CustomerId, request.EstablishmentId, request.StartsOn, request.ExpiresOn, request.Status, modules, request.MaxUsers, request.MaxOrdersPerMonth);
            contracts.Add(contract);
        }
        else
        {
            contract.ApplyPortalState(request.CustomerId, request.EstablishmentId, request.StartsOn, request.ExpiresOn, request.Status, modules, request.MaxUsers, request.MaxOrdersPerMonth);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetStateAsync(tenantId, cancellationToken);
    });

    public async Task<Error?> CheckUserLimitAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var contract = await contracts.FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        if (contract?.MaxUsers is not { } limit)
        {
            return null;
        }

        var current = await users.CountAsync(user => user.TenantId == tenantId && user.IsActive, cancellationToken);

        return current >= limit
            ? Error.Forbidden("contract.user_limit", "O limite de usuários do contrato foi atingido.")
            : null;
    }

    public async Task<Error?> CheckOrderLimitAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var contract = await contracts.FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        if (contract?.MaxOrdersPerMonth is not { } limit)
        {
            return null;
        }

        var now = clock.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var current = await orders.CountAsync(order => order.TenantId == tenantId && order.CreatedAt >= monthStart, cancellationToken);

        return current >= limit
            ? Error.Forbidden("contract.order_limit", "Este estabelecimento atingiu o limite de pedidos do mês.")
            : null;
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.ToOffset(BrazilOffset).DateTime);
}
