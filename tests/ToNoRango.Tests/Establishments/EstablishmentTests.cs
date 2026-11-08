using ToNoRango.Domain.Establishments;

namespace ToNoRango.Tests.Establishments;

public class EstablishmentTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(-3));

    private static Establishment CriarEstabelecimento()
        => new(Guid.NewGuid(), "Açaí do Veloso", "Acai do Veloso", Agora);

    [Fact]
    public void NovoEstabelecimento_RecebeId()
    {
        Assert.NotEqual(Guid.Empty, CriarEstabelecimento().Id);
    }

    [Fact]
    public void Slug_RemoveAcentosEEspacos()
    {
        Assert.Equal("acai-do-veloso", Establishment.NormalizeSlug("  Açaí do Veloso!! "));
    }

    [Fact]
    public void Slug_SemLetrasOuNumeros_Falha()
    {
        Assert.Throws<ArgumentException>(() => Establishment.NormalizeSlug("!!!"));
    }

    [Fact]
    public void Branding_NormalizaCoresParaMaiusculas()
    {
        var establishment = CriarEstabelecimento();

        establishment.SetBranding("#f97316", "#7c3aed", null, null, Agora);

        Assert.Equal("#F97316", establishment.PrimaryColor);
        Assert.Equal("#7C3AED", establishment.SecondaryColor);
    }

    [Fact]
    public void Branding_CorInvalida_Falha()
    {
        var establishment = CriarEstabelecimento();

        Assert.Throws<ArgumentException>(() =>
            establishment.SetBranding("laranja", "#7C3AED", null, null, Agora));
    }

    [Fact]
    public void Fulfillment_SemNenhumaModalidade_Falha()
    {
        var establishment = CriarEstabelecimento();

        Assert.Throws<ArgumentException>(() =>
            establishment.SetFulfillment(false, false, false, 0, 0, Agora));
    }

    [Fact]
    public void Fulfillment_TaxaNegativa_Falha()
    {
        var establishment = CriarEstabelecimento();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            establishment.SetFulfillment(true, true, false, -1, 0, Agora));
    }

    [Fact]
    public void Fulfillment_AtualizaModalidadesETaxas()
    {
        var establishment = CriarEstabelecimento();

        establishment.SetFulfillment(true, true, false, 500, 2000, Agora);

        Assert.True(establishment.IsPickupEnabled);
        Assert.True(establishment.IsDeliveryEnabled);
        Assert.False(establishment.IsDineInEnabled);
        Assert.Equal(500, establishment.DeliveryFeeInCents);
        Assert.Equal(2000, establishment.MinimumOrderInCents);
    }
}
