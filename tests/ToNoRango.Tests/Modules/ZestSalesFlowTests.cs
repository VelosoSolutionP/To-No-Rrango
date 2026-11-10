using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Media;
using ToNoRango.Application.Zest;

namespace ToNoRango.Tests.Modules;

public class ZestSalesFlowTests
{
    private static readonly Guid Burger = Guid.NewGuid();
    private static readonly Guid Acai = Guid.NewGuid();
    private static readonly Guid Size500 = Guid.NewGuid();
    private static readonly Guid Banana = Guid.NewGuid();
    private static readonly Guid Granola = Guid.NewGuid();
    private static readonly Guid Soda = Guid.NewGuid();

    private static SalesCatalog Catalog(long minimum = 0, bool delivery = true, bool pickup = true, bool online = false) => new(
        "https://x/menu/teste",
        [
            new SalesCategory("Lanches", [new SalesItem(Burger, "X Burger", null, 2000, true, [])]),
            new SalesCategory("Açaí", [new SalesItem(Acai, "Açaí Tigela", null, 1500, true,
            [
                new SalesGroup("Tamanho", 1, 1, [new SalesOption(Size500, "500ml", 300, true)]),
                new SalesGroup("Adicionais", 0, 2, [new SalesOption(Banana, "Banana", 100, true), new SalesOption(Granola, "Granola", 200, true)])
            ])]),
            new SalesCategory("Bebidas", [new SalesItem(Soda, "Refrigerante", null, 600, false, [])])
        ],
        delivery,
        pickup,
        minimum,
        "São Paulo",
        "SP",
        "01000000",
        online,
        online);

    private sealed class FakeLookups : ISalesLookups
    {
        public Task<CepData?> FindCepAsync(string cep, CancellationToken cancellationToken) =>
            Task.FromResult<CepData?>(cep == "01000000" ? new CepData(cep, "Rua A", "Centro", "São Paulo", "SP") : null);

        public Task<(long? FeeInCents, string? Error)> QuoteAsync(string district, CancellationToken cancellationToken) =>
            Task.FromResult<(long?, string?)>(district == "Longe" ? (null, "Ainda não entregamos neste bairro.") : (700, null));
    }

    private static async Task<SalesTurn> Say(SalesState state, string message, SalesCatalog? catalog = null, string? name = "Carla") =>
        await ZestSalesFlow.HandleAsync(state, message, catalog ?? Catalog(), new FakeLookups(), name, TestContext.Current.CancellationToken);

    private static async Task<SalesTurn> ConversationWith(SalesCatalog? catalog, string? name, params string[] messages)
    {
        var turn = new SalesTurn(new SalesState(), string.Empty);

        foreach (var message in messages)
        {
            turn = await Say(turn.State, message, catalog, name);
        }

        return turn;
    }

    private static Task<SalesTurn> Conversation(params string[] messages) => ConversationWith(null, "Carla", messages);

    [Theory]
    [InlineData("quero pedir", true)]
    [InlineData("quero 2 x burger", true)]
    [InlineData("quero saber o horário", false)]
    [InlineData("quanto custa o x burger", false)]
    public void IntencaoDeCompra_EDetectada(string message, bool expected) =>
        Assert.Equal(expected, ZestSalesFlow.WantsToOrder(message, Catalog()));

    [Fact]
    public async Task AdicionalForaDoCardapio_ERecusadoSemPerderOCarrinho()
    {
        var turn = await Conversation("quero 2 x burger", "com bacon extra");

        Assert.Equal(SalesStep.CartReview, turn.State.Step);
        Assert.Contains("não está no nosso cardápio", turn.Reply);
        Assert.Equal(2, turn.State.Cart[0].Quantity);
    }

    [Fact]
    public async Task SegundoErro_NaoRepeteAMesmaPergunta()
    {
        var primeira = await Conversation("quero 2 x burger", "asdfgh");
        var segunda = await Say(primeira.State, "asdfgh");

        Assert.NotEqual(primeira.Reply, segunda.Reply);
        Assert.Contains("atendente", segunda.Reply);
    }

    [Fact]
    public async Task RespostaEntendida_ZeraOsErros()
    {
        var erro = await Conversation("quero 2 x burger", "asdfgh");
        var certa = await Say(erro.State, "carrinho");

        Assert.Equal(0, certa.State.Misses);
    }

    [Fact]
    public async Task PedidoDireto_ComQuantidadeVaiParaOCarrinho()
    {
        var turn = await Conversation("quero 2 x burger");

        Assert.Equal(SalesStep.CartReview, turn.State.Step);
        Assert.Equal(2, turn.State.Cart[0].Quantity);
        Assert.Contains("R$ 40,00", turn.Reply);
    }

    [Fact]
    public async Task Cardapio_NaoOfereceCategoriaSemItemDisponivel()
    {
        var turn = await Conversation("quero pedir");

        Assert.Equal(SalesStep.Category, turn.State.Step);
        Assert.DoesNotContain("Bebidas", turn.Reply);
    }

    [Fact]
    public async Task ItemIndisponivel_NaoEVendidoPorNome()
    {
        var turn = await Conversation("quero 1 refrigerante");

        Assert.Empty(turn.State.Cart);
    }

    [Fact]
    public async Task Opcoes_SomamPrecoEGuardamIds()
    {
        var turn = await Conversation("quero pedir", "2", "1", "1", "1,2", "1");

        var line = Assert.Single(turn.State.Cart);
        Assert.Equal(2100, line.UnitPriceInCents);
        Assert.Equal([Size500, Banana, Granola], line.OptionIds);
        Assert.Equal("500ml, Banana, Granola", line.OptionsLabel);
    }

    [Fact]
    public async Task OpcaoObrigatoria_NaoPodeSerPulada()
    {
        var turn = await Conversation("quero pedir", "2", "1", "0");

        Assert.Equal(SalesStep.Options, turn.State.Step);
        Assert.Equal(0, turn.State.PendingGroup);
        Assert.True(turn.Unrecognized);
    }

    [Fact]
    public async Task Adicionais_PodemSerPuladosEMaximoERespeitado()
    {
        var skipped = await Conversation("quero pedir", "2", "1", "1", "0");
        Assert.Equal(SalesStep.Quantity, skipped.State.Step);
    }

    [Fact]
    public async Task PedidoMinimo_BloqueiaFinalizacao()
    {
        var turn = await ConversationWith(Catalog(minimum: 5000), "Carla", "quero 1 x burger", "2");

        Assert.Equal(SalesStep.CartReview, turn.State.Step);
        Assert.Contains("mínimo", turn.Reply);
    }

    [Fact]
    public async Task Entrega_PeloCepCalculaTaxaEFechaResumo()
    {
        var turn = await Conversation("quero 1 x burger", "2", "1", "01000-000", "123 apto 4", "perto da padaria", "1");

        Assert.Equal(SalesStep.Confirm, turn.State.Step);
        Assert.Equal("Rua A", turn.State.Street);
        Assert.Equal("apto 4", turn.State.Complement);
        Assert.Equal("perto da padaria", turn.State.Reference);
        Assert.Contains("Referência: perto da padaria", turn.Reply);
        Assert.Equal(700, turn.State.DeliveryFeeInCents);
        Assert.Contains("R$ 27,00", turn.Reply);
    }

    [Fact]
    public async Task EnderecoManual_RecusaBairroForaDaArea()
    {
        var turn = await Conversation("quero 1 x burger", "2", "1", "sem cep", "Rua B, 10, Longe");

        Assert.Equal(SalesStep.ManualAddress, turn.State.Step);
        Assert.Contains("não entregamos", turn.Reply);
    }

    [Fact]
    public async Task Dinheiro_RegistraTroco()
    {
        var turn = await Conversation("quero 1 x burger", "2", "2", "2", "50");

        Assert.Equal("Troco para 50", turn.State.Notes);
        Assert.Equal(SalesStep.Confirm, turn.State.Step);
    }

    [Fact]
    public async Task SemNomeDoWhatsApp_PerguntaAntesDeConfirmar()
    {
        var turn = await ConversationWith(null, null, "quero 1 x burger", "2", "2", "3");
        Assert.Equal(SalesStep.Name, turn.State.Step);

        var named = await Say(turn.State, "Bia", name: null);
        Assert.Equal(SalesStep.Confirm, named.State.Step);
    }

    [Fact]
    public async Task SoAoConfirmar_PedeFechamento()
    {
        var before = await Conversation("quero 1 x burger", "2", "2", "1");
        Assert.False(before.Checkout);

        Assert.True((await Say(before.State, "1")).Checkout);
    }

    [Fact]
    public async Task Alterar_VoltaAoCarrinhoERemoveItem()
    {
        var turn = await Conversation("quero 1 x burger", "2", "2", "1", "2", "remover 1");

        Assert.Empty(turn.State.Cart);
        Assert.Equal(SalesStep.Category, turn.State.Step);
    }

    [Fact]
    public async Task Cancelar_LimpaAVenda()
    {
        var turn = await Conversation("quero 1 x burger", "cancelar");

        Assert.Equal(SalesStep.Idle, turn.State.Step);
        Assert.Empty(turn.State.Cart);
    }

    [Fact]
    public async Task SemEntrega_VaiDiretoParaRetirada()
    {
        var turn = await ConversationWith(Catalog(delivery: false), "Carla", "quero 1 x burger", "2");

        Assert.Equal(SalesStep.Payment, turn.State.Step);
        Assert.Equal("Pickup", turn.State.Fulfillment);
    }

    [Fact]
    public async Task SemMercadoPago_MenuDePagamentoContinuaOMesmo()
    {
        var turn = await Conversation("quero 1 x burger", "2", "2");

        Assert.Equal(SalesStep.Payment, turn.State.Step);
        Assert.Equal("Como vai pagar?\n\n*1* Pix\n*2* Dinheiro\n*3* Cartão na entrega", ZestSalesFlow.Prompt(turn.State, Catalog()));

        var linkIgnored = await Say(turn.State, "link");
        Assert.Null(linkIgnored.State.Payment);

        var card = await Say(linkIgnored.State, "cartão");
        Assert.Equal("CardOnDelivery", card.State.Payment);
    }

    [Theory]
    [InlineData("1", "Pix")]
    [InlineData("2", "Checkout")]
    [InlineData("3", "Screen")]
    [InlineData("5", "CardOnDelivery")]
    [InlineData("manda o link", "Checkout")]
    [InlineData("mercado pago", "Checkout")]
    [InlineData("cartão", "Checkout")]
    [InlineData("pela tela", "Screen")]
    [InlineData("pix", "Pix")]
    [InlineData("cartão na entrega", "CardOnDelivery")]
    [InlineData("maquininha", "CardOnDelivery")]
    public async Task ComMercadoPago_EscolheComoPagar(string message, string expected)
    {
        var catalog = Catalog(online: true);
        var turn = await ConversationWith(catalog, "Carla", "quero 1 x burger", "2", "2");

        Assert.Contains("*2* Link do Mercado Pago", ZestSalesFlow.Prompt(turn.State, catalog));

        var chosen = await Say(turn.State, message, catalog);

        Assert.Equal(expected, chosen.State.Payment);
        Assert.Equal(SalesStep.Confirm, chosen.State.Step);
    }

    [Fact]
    public async Task ComMercadoPago_DinheiroAindaPerguntaOTroco()
    {
        var catalog = Catalog(online: true);
        var turn = await ConversationWith(catalog, "Carla", "quero 1 x burger", "2", "2", "4");

        Assert.Equal("Cash", turn.State.Payment);
        Assert.Equal(SalesStep.Change, turn.State.Step);
    }

    [Fact]
    public async Task ComMercadoPago_ResumoMostraOLinkEscolhido()
    {
        var catalog = Catalog(online: true);
        var turn = await ConversationWith(catalog, "Carla", "quero 1 x burger", "2", "2", "2");

        Assert.Contains("Pagamento: Link do Mercado Pago", turn.Reply);
    }

    [Fact]
    public void Foto_ReconheceFormatoPeloConteudo()
    {
        Assert.Equal("image/jpeg", MediaService.DetectContentType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/png", MediaService.DetectContentType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("image/webp", MediaService.DetectContentType("RIFF0000WEBP"u8));
        Assert.Null(MediaService.DetectContentType("<svg onload=alert(1)>"u8));
    }
}
