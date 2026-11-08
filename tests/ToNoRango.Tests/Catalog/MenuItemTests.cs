using ToNoRango.Domain.Catalog;

namespace ToNoRango.Tests.Catalog;

public class MenuItemTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(-3));

    private static MenuItem CriarItem(bool isAvailable = true, long priceInCents = 1000)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pizza Grande",
            "Pizza de mussarela",
            null,
            priceInCents,
            isAvailable,
            0,
            Agora);

    [Fact]
    public void NovoItem_RecebeId()
    {
        Assert.NotEqual(Guid.Empty, CriarItem().Id);
    }

    [Fact]
    public void GrupoObrigatorio_AjustaMinimoParaUm()
    {
        var item = CriarItem();

        item.ReplaceOptionGroups(new[]
        {
            new MenuOptionGroupDefinition("Tamanho", 0, 1, true, 0,
                new[] { new MenuOptionDefinition("Grande", 0, true, 0) })
        }, Agora);

        var group = Assert.Single(item.OptionGroups);
        Assert.True(group.IsRequired);
        Assert.Equal(1, group.MinSelections);
    }

    [Fact]
    public void GrupoComMinimoMaiorQueMaximo_Falha()
    {
        var item = CriarItem();

        Assert.Throws<ArgumentException>(() => item.ReplaceOptionGroups(new[]
        {
            new MenuOptionGroupDefinition("Extras", 2, 1, false, 0,
                new[] { new MenuOptionDefinition("Bacon", 300, true, 0) })
        }, Agora));
    }

    [Fact]
    public void GrupoComMaximoZero_Falha()
    {
        var item = CriarItem();

        Assert.Throws<ArgumentOutOfRangeException>(() => item.ReplaceOptionGroups(new[]
        {
            new MenuOptionGroupDefinition("Extras", 0, 0, false, 0,
                new[] { new MenuOptionDefinition("Bacon", 300, true, 0) })
        }, Agora));
    }

    [Fact]
    public void SubstituirGrupos_CriaOpcoesComPreco()
    {
        var item = CriarItem();

        item.ReplaceOptionGroups(new[]
        {
            new MenuOptionGroupDefinition("Extras", 0, 2, false, 0, new[]
            {
                new MenuOptionDefinition("Bacon", 300, true, 0),
                new MenuOptionDefinition("Queijo", 250, true, 1)
            })
        }, Agora);

        var group = Assert.Single(item.OptionGroups);
        Assert.Equal(2, group.Options.Count);
        Assert.Equal(300, group.Options[0].PriceInCents);
        Assert.Equal("Queijo", group.Options[1].Name);
    }

    [Fact]
    public void Disponibilidade_AtualizaSemAlterarAtivo()
    {
        var item = CriarItem();

        item.SetAvailability(false, Agora);

        Assert.False(item.IsAvailable);
        Assert.True(item.IsActive);
    }
}
