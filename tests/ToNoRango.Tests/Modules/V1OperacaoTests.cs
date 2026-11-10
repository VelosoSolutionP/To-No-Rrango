using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Zest;
using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Feedback;
using ToNoRango.Domain.Orders;
using ToNoRango.Domain.Pdv;
using ToNoRango.Infrastructure.Integrations;

namespace ToNoRango.Tests.Modules;

public class V1OperacaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EstablishmentId = Guid.NewGuid();
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static Order Pedido(PaymentMethod method = PaymentMethod.Cash)
    {
        var order = Order.Create(TenantId, EstablishmentId, "TR-1", "Ana", "11999999999", FulfillmentType.Pickup, method,
            null, null, null, null, null, null, null, null, null, 0, "teste", Agora);
        var lanche = new MenuItem(TenantId, EstablishmentId, Guid.NewGuid(), "X Burger", null, null, 2000, true, 0, Agora);
        var suco = new MenuItem(TenantId, EstablishmentId, Guid.NewGuid(), "Suco", null, null, 800, true, 0, Agora);
        order.AddItem(lanche, 1, null, null, PrepStation.Kitchen);
        order.AddItem(suco, 1, null, null, PrepStation.Bar);
        return order;
    }

    // ---------- Horário de funcionamento ----------

    [Fact]
    public void Horario_AbertoDentroDaFaixaEFechadoFora()
    {
        // Terça 10/11/2026: 12:00 UTC = 09:00 em São Paulo.
        const string hours = "2=11:00-15:00,18:00-23:00";

        Assert.False(OpeningSchedule.IsOpen(hours, Agora, SaoPaulo));
        Assert.True(OpeningSchedule.IsOpen(hours, Agora.AddHours(3), SaoPaulo));
        Assert.False(OpeningSchedule.IsOpen(hours, Agora.AddHours(7), SaoPaulo));
        Assert.True(OpeningSchedule.IsOpen(hours, Agora.AddHours(10), SaoPaulo));
    }

    [Fact]
    public void Horario_FaixaQuePassaDaMeiaNoite()
    {
        // Segunda das 18h às 02h: terça 01:00 em SP (04:00 UTC) ainda está aberto.
        const string hours = "1=18:00-02:00";

        Assert.True(OpeningSchedule.IsOpen(hours, new DateTimeOffset(2026, 11, 10, 4, 0, 0, TimeSpan.Zero), SaoPaulo));
        Assert.False(OpeningSchedule.IsOpen(hours, new DateTimeOffset(2026, 11, 10, 6, 0, 0, TimeSpan.Zero), SaoPaulo));
    }

    [Fact]
    public void Horario_ProximaAberturaEDescricao()
    {
        const string hours = "2=11:00-15:00;3=11:00-15:00";

        var next = OpeningSchedule.NextOpening(hours, Agora, SaoPaulo);

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 14, 0, 0, TimeSpan.Zero), next);
        Assert.Contains("Ter: 11:00 às 15:00", OpeningSchedule.Describe(hours));
        Assert.True(OpeningSchedule.IsOpen(null, Agora, SaoPaulo));
        Assert.Throws<ArgumentException>(() => OpeningSchedule.Parse("9=10:00-12:00"));
        Assert.Equal("1=08:00-12:00,14:00-18:00", OpeningSchedule.Normalize("1=14:00-18:00,08:00-12:00"));
    }

    // ---------- Acompanhamento por item ----------

    [Fact]
    public void Item_AvancaPorEtapasEColocaPedidoEmPreparo()
    {
        var order = Pedido();
        order.ChangeStatus(OrderStatus.Confirmed, "caixa", null, Agora);
        var lanche = order.Items[0];
        var suco = order.Items[1];

        Assert.Equal(ItemPrepStatus.Queued, lanche.PrepStatus);
        Assert.Equal(ItemPrepStatus.Received, suco.PrepStatus);

        var allReady = order.AdvanceItem(lanche.Id, ItemPrepStatus.Preparing, "cozinha", Agora);

        Assert.False(allReady);
        Assert.Equal(OrderStatus.Preparing, order.Status);
        Assert.Throws<InvalidOperationException>(() => order.AdvanceItem(lanche.Id, ItemPrepStatus.Queued, "cozinha", Agora));
        Assert.Throws<InvalidOperationException>(() => order.AdvanceItem(suco.Id, ItemPrepStatus.Finishing, "bar", Agora));

        order.AdvanceItem(suco.Id, ItemPrepStatus.Ready, "bar", Agora);
        Assert.True(order.AdvanceItem(lanche.Id, ItemPrepStatus.Ready, "cozinha", Agora));
    }

    [Fact]
    public void Item_AcompanhaOPedidoAteEntregue()
    {
        var order = Pedido();
        order.ChangeStatus(OrderStatus.Confirmed, "caixa", null, Agora);
        order.ChangeStatus(OrderStatus.Preparing, "caixa", null, Agora);
        order.ChangeStatus(OrderStatus.Ready, "caixa", null, Agora);

        Assert.All(order.Items, item => Assert.Equal(ItemPrepStatus.Ready, item.PrepStatus));

        order.ChangeStatus(OrderStatus.Delivered, "caixa", null, Agora);
        Assert.All(order.Items, item => Assert.Equal(ItemPrepStatus.Delivered, item.PrepStatus));
    }

    [Fact]
    public void Item_NaoMudaEmPedidoPendente()
    {
        var order = Pedido();
        Assert.Throws<InvalidOperationException>(() => order.AdvanceItem(order.Items[0].Id, ItemPrepStatus.Preparing, "cozinha", Agora));
    }

    // ---------- Desconto ----------

    [Fact]
    public void Desconto_ReduzTotalEPedeMotivo()
    {
        var order = Pedido();

        Assert.Throws<ArgumentException>(() => order.ApplyDiscount(500, null, Agora));
        Assert.Throws<ArgumentOutOfRangeException>(() => order.ApplyDiscount(5000, "demais", Agora));

        order.ApplyDiscount(500, "cliente fiel", Agora);
        Assert.Equal(2300, order.TotalInCents);
    }

    // ---------- Caixa ----------

    [Fact]
    public void Caixa_ContaSoODinheiroNaGaveta()
    {
        var session = new CashSession(TenantId, EstablishmentId, "ana", 10000, Agora);
        var cash = Pedido(PaymentMethod.Cash);
        var card = Pedido(PaymentMethod.CardOnDelivery);

        session.RegisterSale(cash, "ana", Agora);
        session.RegisterSale(card, "ana", Agora);
        session.Supply(5000, "troco", "ana", Agora);
        session.Withdraw(3000, "depósito", "ana", Agora);

        Assert.Equal(10000 + 2800 + 5000 - 3000, session.ExpectedCash);
        Assert.Throws<InvalidOperationException>(() => session.RegisterSale(cash, "ana", Agora));
        Assert.Throws<InvalidOperationException>(() => session.Withdraw(1_000_000, "tudo", "ana", Agora));
    }

    [Fact]
    public void Caixa_EstornoTiraODinheiroEFechamentoRegistraDiferenca()
    {
        var session = new CashSession(TenantId, EstablishmentId, "ana", 10000, Agora);
        var cash = Pedido(PaymentMethod.Cash);
        session.RegisterSale(cash, "ana", Agora);
        session.ReverseSale(cash, "gerente", Agora);

        Assert.Equal(10000, session.ExpectedCash);
        Assert.Throws<InvalidOperationException>(() => session.ReverseSale(cash, "gerente", Agora));
        Assert.Throws<ArgumentException>(() => session.Close(9500, "ana", null, Agora));

        session.Close(9500, "ana", "faltou troco", Agora);

        Assert.Equal(CashSessionStatus.Closed, session.Status);
        Assert.Equal(-500, session.DifferenceInCents);
        Assert.Throws<InvalidOperationException>(() => session.Supply(100, "x", "ana", Agora));
    }

    // ---------- Avaliação ----------

    [Fact]
    public void Avaliacao_NotasDeUmACinco()
    {
        var rating = new OrderRating(TenantId, EstablishmentId, Guid.NewGuid(), "TR-1", 5, 4, 3, "bom", Guid.NewGuid(), "João", 5, 2, null, null, Agora);

        Assert.Equal(4, rating.StoreAverage);
        Assert.Equal(3.5, rating.CourierAverage);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderRating(TenantId, EstablishmentId, Guid.NewGuid(), "TR-1", 6, 4, 3, null, null, null, null, null, null, null, Agora));
    }

    [Fact]
    public void Avaliacao_SemEntregadorIgnoraNotasDeEntrega()
    {
        var rating = new OrderRating(TenantId, EstablishmentId, Guid.NewGuid(), "TR-1", 5, 5, 5, null, null, null, 1, 1, 1, "x", Agora);

        Assert.Null(rating.CourierServiceScore);
        Assert.Null(rating.CourierAverage);
    }

    // ---------- Promoção ----------

    [Fact]
    public void Promocao_SoValeComPrecoDeMaiorQueOAtual()
    {
        var item = new MenuItem(TenantId, EstablishmentId, Guid.NewGuid(), "X", null, null, 2000, true, 0, Agora);

        item.SetCompareAtPrice(2500, Agora);
        Assert.True(item.IsOnPromotion);

        item.SetCompareAtPrice(1500, Agora);
        Assert.False(item.IsOnPromotion);
        Assert.Null(item.CompareAtPriceInCents);
    }

    // ---------- Voz ----------

    [Fact]
    public void Voz_TiraFormatacaoELinks()
    {
        var spoken = PiperTextToSpeech.ForSpeech("*Pedido* recebido!\nAcompanhe: https://x.com/pedido/1\n*1* Confirmar");

        Assert.DoesNotContain("*", spoken);
        Assert.DoesNotContain("https", spoken);
        Assert.Contains("o link que mandei por escrito", spoken);
    }

    [Theory]
    [InlineData("[BLANK_AUDIO]", null)]
    [InlineData("(música)", null)]
    [InlineData("  quero dois x burger  ", "quero dois x burger")]
    public void Transcricao_DescartaSilencio(string heard, string? expected) =>
        Assert.Equal(expected, WhisperSpeechToText.Clean(heard));
}

public class ZestSalesFlowV1Tests
{
    private static readonly Guid Burger = Guid.NewGuid();
    private static readonly Guid Bacon = Guid.NewGuid();
    private static readonly Guid Combo = Guid.NewGuid();
    private static readonly Guid Soda = Guid.NewGuid();

    private static SalesCatalog Catalog(SavedAddress? saved = null, bool open = true) => new(
        "https://x/menu/teste",
        [
            new SalesCategory("Lanches",
            [
                new SalesItem(Burger, "X Burger", "pão, carne e queijo", 2000, true, []),
                new SalesItem(Bacon, "X Bacon", "com bacon crocante", 2400, true, [], CompareAtPriceInCents: 2900),
                new SalesItem(Combo, "Combo Casal", null, 5000, true, [], ComboText: "Leva: 2x X Burger, 1x Refrigerante")
            ]),
            new SalesCategory("Bebidas",
            [
                new SalesItem(Soda, "Refrigerante Lata", null, 600, true, []),
                new SalesItem(Guid.NewGuid(), "Refrigerante 2L", null, 1200, true, [])
            ])
        ],
        true,
        true,
        0,
        "São Paulo",
        "SP",
        "01000000",
        SavedAddress: saved,
        IsOpen: open,
        ScheduledText: open ? null : "amanhã às 11:00");

    private sealed class FakeLookups : ISalesLookups
    {
        public Task<CepData?> FindCepAsync(string cep, CancellationToken cancellationToken) =>
            Task.FromResult<CepData?>(new CepData(cep, "Rua A", "Centro", "São Paulo", "SP"));

        public Task<(long? FeeInCents, string? Error)> QuoteAsync(string district, CancellationToken cancellationToken) =>
            Task.FromResult<(long?, string?)>((500, null));
    }

    private static async Task<SalesTurn> Talk(SalesCatalog catalog, params string[] messages)
    {
        var turn = new SalesTurn(new SalesState(), string.Empty);

        foreach (var message in messages)
        {
            turn = await ZestSalesFlow.HandleAsync(turn.State, message, catalog, new FakeLookups(), "Carla", TestContext.Current.CancellationToken);
        }

        return turn;
    }

    [Fact]
    public async Task Busca_ListaOsItensQueCombinam()
    {
        Assert.True(ZestSalesFlow.WantsToOrder("buscar bacon", Catalog()));

        var turn = await Talk(Catalog(), "buscar refrigerante");

        Assert.Equal(SalesStep.Item, turn.State.Step);
        Assert.Contains("Refrigerante Lata", turn.Reply);
        Assert.Contains("Refrigerante 2L", turn.Reply);

        var chosen = await Talk(Catalog(), "buscar refrigerante", "1");
        Assert.Equal(SalesStep.Quantity, chosen.State.Step);
        Assert.Equal(Soda, chosen.State.PendingItemId);

        var single = await Talk(Catalog(), "quero pedir", "buscar crocante");
        Assert.Equal(Bacon, single.State.PendingItemId);
    }

    [Fact]
    public async Task Busca_SemResultadoAvisaEMostraCategorias()
    {
        var turn = await Talk(Catalog(), "buscar pizza");

        Assert.Contains("Não encontrei nada", turn.Reply);
        Assert.Equal(SalesStep.Category, turn.State.Step);
    }

    [Fact]
    public async Task Promocoes_MostramPrecoDeEPor()
    {
        var promo = await Talk(Catalog(), "quero pedir", "promoções");
        Assert.Equal(Bacon, promo.State.PendingItemId);

        var list = await Talk(Catalog(), "quero pedir", "1");
        Assert.Contains("~R$ 29,00~ *R$ 24,00*", list.Reply);
    }

    [Fact]
    public async Task Combos_MostramOQueLeva()
    {
        var turn = await Talk(Catalog(), "quero pedir", "1");

        Assert.Contains("Leva: 2x X Burger", turn.Reply);
    }

    [Fact]
    public async Task Menu_TemLinkDoCardapioEAtalhos()
    {
        var turn = await Talk(Catalog(), "quero pedir");

        Assert.Contains("https://x/menu/teste", turn.Reply);
        Assert.Contains("*promoções*", turn.Reply);
        Assert.Contains("*combos*", turn.Reply);
    }

    [Fact]
    public async Task ClienteAntigo_ConfirmaEnderecoSalvo()
    {
        var saved = new SavedAddress("Rua B", "45", "casa 2", "Vila", "São Paulo", "SP", "01000000", "portão azul");
        var turn = await Talk(Catalog(saved), "quero 1 x burger", "2", "1", "1");

        Assert.Equal(SalesStep.SavedAddress, (await Talk(Catalog(saved), "quero 1 x burger", "2", "1")).State.Step);
        Assert.Equal(SalesStep.Payment, turn.State.Step);
        Assert.Equal("Rua B", turn.State.Street);
        Assert.Equal("portão azul", turn.State.Reference);
        Assert.Equal(500, turn.State.DeliveryFeeInCents);
    }

    [Fact]
    public async Task ClienteAntigo_PodeUsarOutroEndereco()
    {
        var saved = new SavedAddress("Rua B", "45", null, "Vila", "São Paulo", "SP", "01000000", null);
        var turn = await Talk(Catalog(saved), "quero 1 x burger", "2", "1", "2");

        Assert.Equal(SalesStep.Cep, turn.State.Step);
    }

    [Fact]
    public async Task Fechado_AvisaAgendamento()
    {
        var turn = await Talk(Catalog(open: false), "quero pedir");

        Assert.Contains("Estamos fechados agora", turn.Reply);
        Assert.Contains("amanhã às 11:00", turn.Reply);

        var confirm = await Talk(Catalog(open: false), "quero 1 x burger", "2", "2", "1");
        Assert.Equal(SalesStep.Confirm, confirm.State.Step);
        Assert.Contains("Agendado para amanhã às 11:00", confirm.Reply);
    }
}
