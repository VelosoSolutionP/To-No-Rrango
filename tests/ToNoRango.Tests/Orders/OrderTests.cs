using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Tests.Orders;

public class OrderTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(-3));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EstablishmentId = Guid.NewGuid();

    private static Order CriarPedido(
        FulfillmentType fulfillmentType = FulfillmentType.Pickup,
        long deliveryFeeInCents = 0,
        string? deliveryStreet = "Rua das Flores")
        => Order.Create(
            TenantId,
            EstablishmentId,
            "TR-20261104-ABCDEF12",
            "Ana Silva",
            "11999999999",
            fulfillmentType,
            PaymentMethod.Pix,
            deliveryStreet,
            "10",
            null,
            "Centro",
            "São Paulo",
            "sp",
            "01000-000",
            "Sem cebola",
            "idem-123",
            deliveryFeeInCents,
            "teste",
            Agora);

    private static MenuItem CriarItem(
        Guid? tenantId = null,
        Guid? establishmentId = null,
        bool isAvailable = true,
        bool baconAvailable = true)
    {
        var item = new MenuItem(
            tenantId ?? TenantId,
            establishmentId ?? EstablishmentId,
            Guid.NewGuid(),
            "Pizza Grande",
            "Pizza de mussarela",
            null,
            1000,
            isAvailable,
            0,
            Agora);

        item.ReplaceOptionGroups(new[]
        {
            new MenuOptionGroupDefinition("Tamanho", 1, 1, true, 0,
                new[] { new MenuOptionDefinition("Grande", 0, true, 0) }),
            new MenuOptionGroupDefinition("Extras", 0, 2, false, 1, new[]
            {
                new MenuOptionDefinition("Bacon", 300, baconAvailable, 0),
                new MenuOptionDefinition("Queijo", 250, true, 1)
            })
        }, Agora);

        return item;
    }

    private static Guid[] OpcoesDoItem(MenuItem item, bool comBacon = true)
    {
        var tamanho = item.OptionGroups[0].Options[0].Id;
        var bacon = item.OptionGroups[1].Options[0].Id;
        return comBacon ? new[] { tamanho, bacon } : new[] { tamanho };
    }

    [Fact]
    public void PedidoComItensECalculaTotaisComAdicionaisETaxa()
    {
        var order = CriarPedido(FulfillmentType.Delivery, 500);
        var item = CriarItem();

        order.AddItem(item, 2, null, OpcoesDoItem(item));

        Assert.Single(order.Items);
        Assert.Equal(2600, order.SubtotalInCents);
        Assert.Equal(500, order.DeliveryFeeInCents);
        Assert.Equal(3100, order.TotalInCents);
        Assert.Equal(2, order.Items[0].Quantity);
        Assert.Equal(1300, order.Items[0].UnitPriceInCents);
        Assert.Equal(2, order.Items[0].Options.Count);
    }

    [Fact]
    public void ItemIndisponivel_NaoPodeSerAdicionado()
    {
        var order = CriarPedido();
        var item = CriarItem(isAvailable: false);

        Assert.Throws<InvalidOperationException>(() =>
            order.AddItem(item, 1, null, OpcoesDoItem(item)));
    }

    [Fact]
    public void ItemDeOutroEstabelecimento_NaoPodeSerAdicionado()
    {
        var order = CriarPedido();
        var item = CriarItem(establishmentId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            order.AddItem(item, 1, null, OpcoesDoItem(item)));
    }

    [Fact]
    public void GrupoObrigatorioSemEscolha_Falha()
    {
        var order = CriarPedido();
        var item = CriarItem();

        Assert.Throws<InvalidOperationException>(() =>
            order.AddItem(item, 1, null, Array.Empty<Guid>()));
    }

    [Fact]
    public void OpcaoIndisponivel_Falha()
    {
        var order = CriarPedido();
        var item = CriarItem(baconAvailable: false);

        Assert.Throws<InvalidOperationException>(() =>
            order.AddItem(item, 1, null, OpcoesDoItem(item)));
    }

    [Fact]
    public void OpcaoDeOutroItem_Falha()
    {
        var order = CriarPedido();
        var item = CriarItem();
        var outroItem = CriarItem();

        var opcaoDeOutroItem = outroItem.OptionGroups[1].Options[0].Id;

        Assert.Throws<InvalidOperationException>(() =>
            order.AddItem(item, 1, null, new[] { item.OptionGroups[0].Options[0].Id, opcaoDeOutroItem }));
    }

    [Fact]
    public void QuantidadeForaDoLimite_Falha()
    {
        var order = CriarPedido();
        var item = CriarItem();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.AddItem(item, 0, null, OpcoesDoItem(item)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.AddItem(item, 100, null, OpcoesDoItem(item)));
    }

    [Fact]
    public void DeliverySemEndereco_Falha()
    {
        Assert.Throws<ArgumentException>(() =>
            CriarPedido(FulfillmentType.Delivery, 500, deliveryStreet: null));
    }

    [Fact]
    public void PickupPermiteConcluirSemPassarPorEntrega()
    {
        var order = CriarPedido();

        order.ChangeStatus(OrderStatus.Confirmed, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Preparing, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Ready, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Delivered, "operador", null, Agora);

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.ConfirmedAt);
        Assert.NotNull(order.ReadyAt);
        Assert.NotNull(order.DeliveredAt);
        Assert.Equal(5, order.StatusHistory.Count);
    }

    [Fact]
    public void DeliveryPermiteSairParaEntrega()
    {
        var order = CriarPedido(FulfillmentType.Delivery, 500);

        order.ChangeStatus(OrderStatus.Confirmed, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Preparing, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Ready, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.OutForDelivery, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Delivered, "operador", null, Agora);

        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void TransicaoInvalida_Falha()
    {
        var order = CriarPedido();

        Assert.Throws<InvalidOperationException>(() =>
            order.ChangeStatus(OrderStatus.Preparing, "operador", null, Agora));
    }

    [Fact]
    public void CancelamentoExigeMotivo()
    {
        var order = CriarPedido();

        Assert.Throws<ArgumentException>(() =>
            order.ChangeStatus(OrderStatus.Cancelled, "operador", null, Agora));

        order.ChangeStatus(OrderStatus.Cancelled, "operador", "Cliente desistiu", Agora);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Cliente desistiu", order.CancelReason);
        Assert.Equal("operador", order.CancelledBy);
        Assert.NotNull(order.CancelledAt);
    }

    [Fact]
    public void PedidoEntregueNaoPodeSerCancelado()
    {
        var order = CriarPedido();

        order.ChangeStatus(OrderStatus.Confirmed, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Preparing, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Ready, "operador", null, Agora);
        order.ChangeStatus(OrderStatus.Delivered, "operador", null, Agora);

        Assert.Throws<InvalidOperationException>(() =>
            order.ChangeStatus(OrderStatus.Cancelled, "operador", "Tentativa tardia", Agora));
    }

    [Fact]
    public void PagamentoAtualizaStatus()
    {
        var order = CriarPedido();

        order.SetPaymentStatus(PaymentStatus.Paid, Agora);

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(Agora, order.UpdatedAt);
    }
}
