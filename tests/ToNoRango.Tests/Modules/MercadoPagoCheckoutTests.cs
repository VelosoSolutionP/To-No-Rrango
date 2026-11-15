using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Payments;
using ToNoRango.Infrastructure.Integrations;

namespace ToNoRango.Tests.Modules;

public class MercadoPagoCheckoutTests
{
    private sealed class Capture(HttpStatusCode status, string answer) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static MercadoPagoGateway Gateway(Capture capture) =>
        new(new HttpClient(capture) { BaseAddress = new Uri("https://api.mercadopago.test/") }, NullLogger<MercadoPagoGateway>.Instance);

    private static CheckoutCharge Charge(string returnUrl = "https://loja.test/pedido/ABC") =>
        new(2590, "Pedido ABC", "checkout:ABC", returnUrl, new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero), "https://loja.test/hook");

    [Fact]
    public async Task Link_MandaValorReferenciaERetorno()
    {
        var capture = new Capture(HttpStatusCode.Created, """{"id":"pref-1","init_point":"https://mp.test/pay/1"}""");

        var checkout = await Gateway(capture).CreateCheckoutAsync("token", Charge(), TestContext.Current.CancellationToken);

        Assert.Equal(new GatewayCheckout("pref-1", "https://mp.test/pay/1"), checkout);
        Assert.Equal("/checkout/preferences", capture.Path);

        using var body = JsonDocument.Parse(capture.Body!);
        var root = body.RootElement;
        Assert.Equal(25.90m, root.GetProperty("items")[0].GetProperty("unit_price").GetDecimal());
        Assert.Equal("checkout:ABC", root.GetProperty("external_reference").GetString());
        Assert.Equal("https://loja.test/pedido/ABC", root.GetProperty("back_urls").GetProperty("success").GetString());
        Assert.Equal("approved", root.GetProperty("auto_return").GetString());
        Assert.Equal("https://loja.test/hook", root.GetProperty("notification_url").GetString());
        Assert.Equal("ticket", root.GetProperty("payment_methods").GetProperty("excluded_payment_types")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Link_SemHttps_NaoPedeRetornoAutomatico()
    {
        var capture = new Capture(HttpStatusCode.Created, """{"id":"pref-1","init_point":"https://mp.test/pay/1"}""");

        await Gateway(capture).CreateCheckoutAsync("token", Charge("http://localhost:3000/pedido/ABC"), TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(capture.Body!);
        Assert.False(body.RootElement.TryGetProperty("auto_return", out _));
    }

    [Fact]
    public async Task Link_RespostaSemEndereco_EhFalhaDoGateway()
    {
        var capture = new Capture(HttpStatusCode.Created, """{"id":"pref-1"}""");

        await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            Gateway(capture).CreateCheckoutAsync("token", Charge(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("checkout:ABC", "ABC")]
    [InlineData("ABC", null)]
    [InlineData(null, null)]
    [InlineData("xcheckout:ABC", null)]
    public void SoReferenciaDeLinkEhAdotada(string? reference, string? expected) =>
        Assert.Equal(expected, PaymentService.OrderCodeOfCheckout(reference));

    [Fact]
    public async Task Pix_VenceNoHorarioPedido_EmHorarioDeBrasilia()
    {
        var capture = new Capture(HttpStatusCode.Created, """{"id":7,"status":"pending","transaction_amount":25.9,"date_of_expiration":"2026-11-14T13:30:00.000-03:00","point_of_interaction":{"transaction_data":{"qr_code":"000201","qr_code_base64":"AAA"}}}""");
        var expires = new DateTimeOffset(2026, 11, 14, 16, 30, 0, TimeSpan.Zero);

        var payment = await Gateway(capture).CreatePixAsync("token", new PixCharge(2590, "Pedido ABC", "ABC", "a@b.c", "pix-1", null, expires), TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(capture.Body!);
        Assert.Equal("2026-11-14T13:30:00.000-03:00", body.RootElement.GetProperty("date_of_expiration").GetString());
        Assert.Equal(expires, payment.ExpiresAt);
    }

    [Fact]
    public void Pix_DuraTrintaMinutos_OMinimoDoMercadoPago() => Assert.Equal(TimeSpan.FromMinutes(30), PaymentService.PixLifetime);
}
