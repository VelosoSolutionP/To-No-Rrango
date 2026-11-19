using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Orders;

namespace ToNoRango.Tests.Orders;

/// <summary>Pedido de valor fechado (WhatsApp/cardápio): o que entra no Checkout com a senha do gerente.</summary>
public class ExtraItemsTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(-3));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EstablishmentId = Guid.NewGuid();

    private static Order Pedido() => Order.Create(
        TenantId, EstablishmentId, "TR-20261119-ABCDEF12", "Ana", "11999999999", FulfillmentType.Pickup, PaymentMethod.Cash,
        null, null, null, null, null, null, null, null, null, 0, "zest", Agora, OrderChannel.WhatsApp);

    private static MenuItem Item(string nome, long preco) =>
        new(TenantId, EstablishmentId, Guid.NewGuid(), nome, null, null, preco, true, 0, Agora);

    [Fact]
    public void Item_extra_soma_no_total_e_fica_no_historico()
    {
        var pedido = Pedido();
        pedido.AddItem(Item("X-Burger", 2500), 1, null, null);
        pedido.ChangeStatus(OrderStatus.Confirmed, "loja", null, Agora);

        var extra = pedido.AddItem(Item("Coca lata", 600), 2, null, null);
        pedido.RecordExtraItems([extra], "caixa@loja", Agora);

        Assert.Equal(3700, pedido.TotalInCents);
        Assert.Equal(ItemPrepStatus.Queued, extra.PrepStatus);
        Assert.Contains(pedido.StatusHistory, h => h.Reason is { } motivo && motivo.Contains("senha do gerente") && motivo.Contains("2x Coca lata") && h.ChangedBy == "caixa@loja");
    }

    [Fact]
    public void Pedido_que_saiu_da_cozinha_nao_aceita_extra()
    {
        var pedido = Pedido();
        pedido.AddItem(Item("X-Burger", 2500), 1, null, null);
        Assert.True(pedido.AcceptsExtraItems);

        pedido.ChangeStatus(OrderStatus.Confirmed, "loja", null, Agora);
        pedido.ChangeStatus(OrderStatus.Preparing, "loja", null, Agora);
        Assert.True(pedido.AcceptsExtraItems);

        pedido.ChangeStatus(OrderStatus.Ready, "loja", null, Agora);
        Assert.False(pedido.AcceptsExtraItems);
    }

    [Fact]
    public void Pedido_pago_nao_aceita_extra()
    {
        var pedido = Pedido();
        pedido.AddItem(Item("X-Burger", 2500), 1, null, null);
        pedido.SetPaymentStatus(PaymentStatus.Paid, Agora);

        Assert.False(pedido.AcceptsExtraItems);
    }
}
