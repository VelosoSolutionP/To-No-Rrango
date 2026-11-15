using ToNoRango.Application.Media;

namespace ToNoRango.Tests.Modules;

public class ProductPhotoQueryTests
{
    [Theory]
    [InlineData("Cerveja Pilsen Lata 350ml - Fardo 12un", "cerveja pilsen lata")]
    [InlineData("Pizza Margherita Grande", "pizza margherita")]
    [InlineData("Açaí 500ml", "açaí")]
    [InlineData("Conserto de Geladeira - Visita Técnica", "conserto geladeira")]
    [InlineData("Combo Burger + Batata + Refri", "combo burger batata refri")]
    [InlineData("Whisky 12 anos 1L", "whisky")]
    public void Busca_o_que_a_foto_mostra_nao_como_e_vendido(string name, string expected) =>
        Assert.Equal(expected, ProductPhotoQuery.Clean(name));

    [Fact]
    public void Sem_resultado_tenta_nomes_mais_curtos() =>
        Assert.Equal(["pizza frango catupiry", "pizza frango", "pizza"], ProductPhotoQuery.Attempts("pizza frango catupiry").ToArray());
}
