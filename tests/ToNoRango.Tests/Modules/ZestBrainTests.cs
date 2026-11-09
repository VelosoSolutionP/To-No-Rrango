using ToNoRango.Application.Zest;

namespace ToNoRango.Tests.Modules;

public class ZestBrainTests
{
    private static ZestContext Context(ZestOrder? order = null, IReadOnlyList<ZestZone>? zones = null) => new(
        "Açaí do Teste",
        "https://x/menu/teste",
        "Rua A, 1",
        true,
        true,
        500,
        0,
        "Pix e dinheiro",
        zones ?? [],
        [new ZestMenuItem("X Burger", 2000, "Lanches", true, null), new ZestMenuItem("Açaí 500ml", 1800, "Açaí", false, null)],
        [new ZestKnowledge("Horário", "Abrimos às 18h.", "horario, abre")],
        order);

    [Theory]
    [InlineData("quero falar com um atendente", ZestIntent.Handoff)]
    [InlineData("cadê meu pedido?", ZestIntent.OrderStatus)]
    [InlineData("que horas abre?", ZestIntent.Knowledge)]
    [InlineData("quanto custa o X-Burger?", ZestIntent.ItemPrice)]
    [InlineData("qual a taxa de entrega", ZestIntent.Delivery)]
    [InlineData("me manda o cardápio", ZestIntent.Menu)]
    [InlineData("onde fica a loja?", ZestIntent.Address)]
    [InlineData("aceita pix?", ZestIntent.Payment)]
    [InlineData("boa noite", ZestIntent.Greeting)]
    [InlineData("qual o sentido da vida", ZestIntent.Unknown)]
    public void Intencao_EReconhecida(string message, ZestIntent intent) =>
        Assert.Equal(intent, ZestBrain.Answer(Context(), message).Intent);

    [Fact]
    public void Preco_UsaValorDoCardapio() =>
        Assert.Contains("R$ 20,00", ZestBrain.Answer(Context(), "quanto custa o x burger").Text);

    [Fact]
    public void ItemIndisponivel_NaoInformaComoDisponivel() =>
        Assert.Contains("indisponível", ZestBrain.Answer(Context(), "tem açaí 500ml?").Text);

    [Fact]
    public void Status_SoRespondeComPedidoDoTelefone()
    {
        Assert.DoesNotContain("TR-1", ZestBrain.Answer(Context(), "meu pedido").Text);
        Assert.Contains("TR-1", ZestBrain.Answer(Context(new ZestOrder("TR-1", "em preparo", "https://x")), "meu pedido").Text);
    }

    [Fact]
    public void Entrega_UsaTaxaDoBairroQuandoExisteRegiao() =>
        Assert.Contains("R$ 7,00", ZestBrain.Answer(Context(zones: [new ZestZone("Vila Nova", 700)]), "taxa de entrega na vila nova").Text);

    [Fact]
    public void PalavraParcial_NaoDisparaIntencao() =>
        Assert.NotEqual(ZestIntent.Greeting, ZestBrain.Answer(Context(), "noite passada").Intent);
}
