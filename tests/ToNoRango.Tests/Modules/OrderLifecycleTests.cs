using System.Linq.Expressions;
using NSubstitute;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Orders;
using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Tests.Modules;

public class OrderLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Establishment = Guid.NewGuid();

    private readonly IRepository<RecipeLine> _recipes = Substitute.For<IRepository<RecipeLine>>();
    private readonly IRepository<StockItem> _stockItems = Substitute.For<IRepository<StockItem>>();
    private readonly IRepository<StockMovement> _movements = Substitute.For<IRepository<StockMovement>>();
    private readonly IRepository<FinancialEntry> _entries = Substitute.For<IRepository<FinancialEntry>>();
    private readonly IStockStore _stock = Substitute.For<IStockStore>();
    private readonly OrderLifecycle _lifecycle;

    public OrderLifecycleTests()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var sender = Substitute.For<IWhatsAppSender>();
        sender.IsConfigured.Returns(false);

        var notifier = new OrderNotifier(
            Substitute.For<IRepository<WhatsAppSettings>>(),
            Substitute.For<IRepository<OutboundMessage>>(),
            sender,
            Substitute.For<IModuleAccess>(),
            Substitute.For<IAppUrls>(),
            clock);

        _lifecycle = new OrderLifecycle(_recipes, _stockItems, _movements, _stock, _entries, notifier, clock);
    }

    private static (Order Order, MenuItem Item) NewOrder(PaymentMethod method = PaymentMethod.Cash)
    {
        var item = new MenuItem(Tenant, Establishment, Guid.NewGuid(), "X Burger", null, null, 2000, true, 0, Now);
        var order = Order.Create(
            Tenant, Establishment, "TR-1", "Ana", "11988887777", FulfillmentType.Pickup, method,
            null, null, null, null, null, null, null, null, null, 0, "teste", Now);
        order.AddItem(item, 3, null, null);

        return (order, item);
    }

    private void Recipe(Guid menuItemId, Guid stockItemId, decimal quantity) =>
        _recipes.ListAsync(
                Arg.Any<Expression<Func<RecipeLine, bool>>>(),
                Arg.Any<Expression<Func<RecipeLine, Guid>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<int>())
            .Returns([new RecipeLine(Tenant, Establishment, menuItemId, stockItemId, quantity)]);

    [Fact]
    public async Task Confirmar_BaixaQuantidadeDaFichaVezesItens()
    {
        var (order, item) = NewOrder();
        var stockItemId = Guid.NewGuid();
        Recipe(item.Id, stockItemId, 2);

        await _lifecycle.ChangeStatusAsync(order, OrderStatus.Confirmed, "teste", null, TestContext.Current.CancellationToken);

        await _stock.Received(1).ApplyDeltaAsync(stockItemId, -6m, Now, Arg.Any<CancellationToken>());
        Assert.NotNull(order.StockDeductedAt);
    }

    [Fact]
    public async Task ItemSemFicha_NaoMexeNoEstoque()
    {
        var (order, _) = NewOrder();

        await _lifecycle.ChangeStatusAsync(order, OrderStatus.Confirmed, "teste", null, TestContext.Current.CancellationToken);

        await _stock.DidNotReceiveWithAnyArgs().ApplyDeltaAsync(default, default, default, TestContext.Current.CancellationToken);
        Assert.Null(order.StockDeductedAt);
    }

    [Fact]
    public async Task CancelarAntesDeConfirmar_NaoEstornaEstoque()
    {
        var (order, _) = NewOrder();

        await _lifecycle.ChangeStatusAsync(order, OrderStatus.Cancelled, "teste", "desistiu", TestContext.Current.CancellationToken);

        await _stock.DidNotReceiveWithAnyArgs().ApplyDeltaAsync(default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PagarDuasVezes_LancaUmaReceita()
    {
        var (order, _) = NewOrder();

        await _lifecycle.MarkPaidAsync(order, TestContext.Current.CancellationToken);
        await _lifecycle.MarkPaidAsync(order, TestContext.Current.CancellationToken);

        _entries.Received(1).Add(Arg.Is<FinancialEntry>(entry =>
            entry.Type == FinancialType.Receivable && entry.Status == FinancialStatus.Settled && entry.AmountInCents == 6000));
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public async Task EntregarPedidoEmDinheiro_MarcaComoPago()
    {
        var (order, _) = NewOrder();

        foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Delivered })
        {
            await _lifecycle.ChangeStatusAsync(order, status, "teste", null, TestContext.Current.CancellationToken);
        }

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public async Task EntregarPedidoPix_NaoPresumePagamento()
    {
        var (order, _) = NewOrder(PaymentMethod.Pix);

        foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Delivered })
        {
            await _lifecycle.ChangeStatusAsync(order, status, "teste", null, TestContext.Current.CancellationToken);
        }

        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
    }
}
