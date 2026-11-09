using ToNoRango.Application.Identity;

namespace ToNoRango.Tests.Identity;

public sealed class VelosoIdProtocolTests
{
    private const string Root = "segredo-raiz-de-teste";
    private const string Nonce = "nonce-do-navegador-123";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Estado_valido_devolve_o_valor_aleatorio()
    {
        var state = VelosoIdProtocol.CreateState(Root, Nonce, Now);

        Assert.Equal(state.Split('.')[0], VelosoIdProtocol.ValidateState(Root, state, Nonce, Now));
    }

    [Fact]
    public void Estado_de_outro_navegador_e_recusado()
        => Assert.Null(VelosoIdProtocol.ValidateState(Root, VelosoIdProtocol.CreateState(Root, Nonce, Now), "outro-nonce-qualquer-1", Now));

    [Fact]
    public void Estado_expirado_e_recusado()
        => Assert.Null(VelosoIdProtocol.ValidateState(Root, VelosoIdProtocol.CreateState(Root, Nonce, Now), Nonce, Now.AddMinutes(11)));

    [Fact]
    public void Estado_adulterado_e_recusado()
    {
        var parts = VelosoIdProtocol.CreateState(Root, Nonce, Now).Split('.');

        Assert.Null(VelosoIdProtocol.ValidateState(Root, $"outro.{parts[1]}.{parts[2]}", Nonce, Now));
    }

    [Fact]
    public void Estado_de_outra_chave_e_recusado()
        => Assert.Null(VelosoIdProtocol.ValidateState("outra-raiz", VelosoIdProtocol.CreateState(Root, Nonce, Now), Nonce, Now));

    [Fact]
    public void Credencial_troca_a_meia_noite_de_Brasilia()
    {
        var before = new DateTimeOffset(2026, 11, 10, 2, 59, 0, TimeSpan.Zero);
        var after = new DateTimeOffset(2026, 11, 10, 3, 1, 0, TimeSpan.Zero);

        Assert.NotEqual(VelosoIdProtocol.DailyCredential(Root, "TO-NO-RANGO", before), VelosoIdProtocol.DailyCredential(Root, "TO-NO-RANGO", after));
    }

    [Fact]
    public void Verificador_tem_o_tamanho_do_PKCE()
        => Assert.InRange(VelosoIdProtocol.Verifier(Root, "abc").Length, 43, 128);
}
