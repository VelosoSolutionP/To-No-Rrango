using ToNoRango.Domain.Contracts;

namespace ToNoRango.Tests.Contracts;

public class ContractTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 2);

    private static Contract CriarVigente(DateOnly? expiresOn = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 11, 1),
            expiresOn,
            ContractStatus.Vigente,
            new[]
            {
                new ContractModule("cardapio", true),
                new ContractModule("pedidos", true)
            });

    [Fact]
    public void ContratoVigenteDentroDaJanela_LiberaAcesso()
    {
        var contract = CriarVigente(new DateOnly(2026, 11, 30));

        Assert.True(contract.IsVigente(Hoje));
        Assert.False(contract.IsBloqueado(Hoje));
    }

    [Fact]
    public void ContratoVigenteSemExpiracao_NaoExpira()
    {
        var contract = CriarVigente(null);

        Assert.True(contract.IsVigente(Hoje));
        Assert.False(contract.IsBloqueado(Hoje));
    }

    [Fact]
    public void ContratoExpiradoNoPassado_BloqueiaAcesso()
    {
        var contract = CriarVigente(new DateOnly(2026, 10, 30));

        Assert.False(contract.IsVigente(Hoje));
        Assert.True(contract.IsBloqueado(Hoje));
    }

    [Fact]
    public void ContratoComStatusExpirado_EBloqueiaMesmoComDataFutura()
    {
        var contract = new Contract(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30),
            ContractStatus.Expirado,
            Array.Empty<ContractModule>());

        Assert.False(contract.IsVigente(Hoje));
        Assert.True(contract.IsBloqueado(Hoje));
    }

    [Fact]
    public void LiberadosModules_ExpoeApenasModulosContratadosEBaixados()
    {
        var contract = new Contract(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 11, 1),
            null,
            ContractStatus.Vigente,
            new[]
            {
                new ContractModule("cardapio", true),
                new ContractModule("pedidos", true),
                new ContractModule("pagamentos", false)
            });

        var liberados = contract.LiberadosModules();

        Assert.Contains("cardapio", liberados);
        Assert.Contains("pedidos", liberados);
        Assert.DoesNotContain("pagamentos", liberados);
    }
}
