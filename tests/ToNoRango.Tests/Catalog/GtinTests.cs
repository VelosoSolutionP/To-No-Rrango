using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using ToNoRango.Application.Catalog;
using ToNoRango.Infrastructure.Integrations;

namespace ToNoRango.Tests.Catalog;

public class GtinTests
{
    [Theory]
    [InlineData("7891000100103", true)]
    [InlineData("7896116900029", true)]
    [InlineData("789-1000-100103", true)]
    [InlineData("7891000100104", false)]
    [InlineData("0000000000000", false)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    public void Confere_o_digito_verificador(string code, bool valid) => GtinCode.IsValid(code).ShouldBe(valid);

    [Fact]
    public async Task Le_produto_ncm_e_foto_e_reaproveita_o_token()
    {
        var handler = new FakeProvider();
        handler.Product("7891000100103", """{"ean":"7891000100103","nome":"LEITE COND MOCA LT 395G","nome_acento":"LEITE COND MOÇA LT 395G","ncm":"04029900","cest":"","marca":"","link_foto":"x"}""");
        var catalog = Catalog(handler);

        var result = await catalog.LookupAsync("7891000100103", withImage: true, CancellationToken.None);

        result.Status.ShouldBe(GtinLookupStatus.Found);
        result.Product!.Name.ShouldBe("LEITE COND MOÇA LT 395G");
        result.Product.Ncm.ShouldBe("04029900");
        result.Product.Cest.ShouldBeNull();
        result.Product.Brand.ShouldBeNull();
        result.Product.Image.ShouldNotBeNull();
        handler.TokenCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Codigo_fora_da_base_vem_como_nao_encontrado()
    {
        var handler = new FakeProvider();
        var result = await Catalog(handler).LookupAsync("7896116900029", withImage: false, CancellationToken.None);

        result.Status.ShouldBe(GtinLookupStatus.NotFound);
    }

    [Fact]
    public async Task Token_vencido_e_renovado_uma_vez()
    {
        var handler = new FakeProvider { RejectFirstBearer = true };
        handler.Product("7896116900029", """{"nome":"FEIJÃO CARIOCA","ncm":"07133319"}""");

        var result = await Catalog(handler).LookupAsync("7896116900029", withImage: false, CancellationToken.None);

        result.Status.ShouldBe(GtinLookupStatus.Found);
        handler.TokenCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Usuario_recusado_vira_mensagem_clara()
    {
        var handler = new FakeProvider { RefuseToken = true };

        var result = await Catalog(handler).LookupAsync("7896116900029", withImage: false, CancellationToken.None);

        result.Status.ShouldBe(GtinLookupStatus.Unavailable);
        result.Message!.ShouldContain("usuário e a senha");
    }

    [Fact]
    public async Task Espaca_as_chamadas_pelo_intervalo_do_plano()
    {
        var handler = new FakeProvider();
        handler.Product("7891000100103", """{"nome":"A"}""");
        handler.Product("7896116900029", """{"nome":"B"}""");
        var catalog = Catalog(handler, intervalSeconds: 1);
        var clock = Stopwatch.StartNew();

        await catalog.LookupAsync("7891000100103", withImage: false, CancellationToken.None);
        await catalog.LookupAsync("7896116900029", withImage: false, CancellationToken.None);

        // token + 2 products = 3 calls, at least 2 intervals apart.
        clock.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(1900));
        handler.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task Resposta_repetida_sai_do_cache_sem_gastar_chamada()
    {
        var handler = new FakeProvider();
        handler.Product("7891000100103", """{"nome":"A"}""");
        var catalog = Catalog(handler);

        await catalog.LookupAsync("7891000100103", withImage: false, CancellationToken.None);
        await catalog.LookupAsync("7891000100103", withImage: false, CancellationToken.None);

        handler.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Foto_generica_de_produto_sem_imagem_nao_e_gravada()
    {
        var handler = new FakeProvider();
        handler.Product("7896116900029", """{"nome":"FEIJÃO","link_foto":"x"}""");
        var placeholder = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(FakeProvider.Png));

        var result = await Catalog(handler, placeholder: placeholder).LookupAsync("7896116900029", withImage: true, CancellationToken.None);

        result.Status.ShouldBe(GtinLookupStatus.Found);
        result.Product!.Image.ShouldBeNull();
    }

    private static RscGtinCatalog Catalog(FakeProvider handler, int intervalSeconds = 0, string? placeholder = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://gtin.test/") },
            new GtinOptions { Username = "u", Password = "p", IntervalSeconds = intervalSeconds, PlaceholderImageSha256 = placeholder is null ? [] : [placeholder] },
            new GtinGate(),
            NullLogger<RscGtinCatalog>.Instance);

    private sealed class FakeProvider : HttpMessageHandler
    {
        public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];
        private readonly Dictionary<string, string> _products = [];
        private int _issued;

        public int Calls { get; private set; }
        public int TokenCalls { get; private set; }
        public bool RejectFirstBearer { get; init; }
        public bool RefuseToken { get; init; }

        public void Product(string code, string json) => _products[code] = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/oauth/token", StringComparison.Ordinal))
            {
                TokenCalls++;
                return Task.FromResult(RefuseToken
                    ? Json(HttpStatusCode.Unauthorized, """{"mensagem":"Credenciais inválidas"}""")
                    : Json(HttpStatusCode.OK, $$"""{"access_token":"t{{++_issued}}","expires_in":3600}"""));
            }

            if (RejectFirstBearer && request.Headers.Authorization?.Parameter == "t1")
            {
                return Task.FromResult(Json(HttpStatusCode.Unauthorized, """{"mensagem":"Token inválido!"}"""));
            }

            var code = path.Split('/')[4];

            if (path.EndsWith("/image", StringComparison.Ordinal))
            {
                var image = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
                image.Content.Headers.ContentType = new("image/png");
                return Task.FromResult(image);
            }

            return Task.FromResult(_products.TryGetValue(code, out var json)
                ? Json(HttpStatusCode.OK, json)
                : Json(HttpStatusCode.NotFound, """{"mensagem":"Não encontrado"}"""));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
