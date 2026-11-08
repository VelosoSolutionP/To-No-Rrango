using ToNoRango.Application.Contracts;
using ToNoRango.Domain.Contracts;

namespace ToNoRango.Tests.Contracts;

public class ContractAccessServiceTests
{
    private readonly ContractAccessService _service = new();

    private static readonly DateOnly Hoje = new(2026, 11, 2);

    [Fact]
    public void ContratoVigente_LiberacaoComModulosDisponiveis()
    {
        var contract = new Contract(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30),
            ContractStatus.Vigente,
            new[]
            {
                new ContractModule("cardapio", true),
                new ContractModule("pedidos", true),
                new ContractModule("fiscal-emissao", false)
            });

        var decision = _service.Evaluate(contract, Hoje);

        Assert.Equal(AccessState.Liberado, decision.State);
        Assert.Equal(ContractStatus.Vigente, decision.ContractStatus);
        Assert.Contains("cardapio", decision.AvailableModules);
        Assert.DoesNotContain("fiscal-emissao", decision.AvailableModules);
        Assert.True(string.IsNullOrEmpty(decision.BlockReason));
    }

    [Fact]
    public void ContratoExpirado_BloqueiaComMotivoDeExpiracao()
    {
        var contract = new Contract(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 10, 30),
            ContractStatus.Expirado,
            Array.Empty<ContractModule>());

        var decision = _service.Evaluate(contract, Hoje);

        Assert.Equal(AccessState.Bloqueado, decision.State);
        Assert.Contains("expirad", decision.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(decision.AvailableModules);
    }

    [Fact]
    public void ContratoAusente_BloqueiaComMotivoDeContratoInvalido()
    {
        var decision = _service.Evaluate(null, Hoje);

        Assert.Equal(AccessState.Bloqueado, decision.State);
        Assert.Contains("contrato", decision.BlockReason, StringComparison.OrdinalIgnoreCase);
    }
}
