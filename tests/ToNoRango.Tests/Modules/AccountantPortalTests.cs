using NSubstitute;
using Shouldly;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Fiscal;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Tests.Modules;

public class AccountantPortalTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class World
    {
        public MemoryRepository<AccountantFirm> Firms { get; } = new();
        public MemoryRepository<AccountantUser> Users { get; } = new();
        public MemoryRepository<AccountantLink> Links { get; } = new();
        public MemoryRepository<AccountantMessage> Messages { get; } = new();
        public MemoryRepository<FiscalApiKey> Keys { get; } = new();
        public MemoryRepository<FiscalWebhook> Webhooks { get; } = new();
        public Establishment Store { get; }
        public AccountantPortalService Portal { get; }
        public AccountantDeskService Desk { get; }

        public World()
        {
            Store = (Establishment)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Establishment));
            typeof(Establishment).GetProperty(nameof(Establishment.Id))!.SetValue(Store, Guid.NewGuid());
            typeof(Establishment).GetProperty(nameof(Establishment.Name))!.SetValue(Store, "Açaí do Veloso");

            var hasher = Substitute.For<IPasswordHasher>();
            hasher.Hash(Arg.Any<string>()).Returns(call => "h:" + call.Arg<string>());
            hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(call => call.ArgAt<string>(1) == "h:" + call.ArgAt<string>(0));
            var tokens = Substitute.For<ITokenIssuer>();
            tokens.IssueAccountant(default, default, default!, default!, default!).ReturnsForAnyArgs(("token", DateTimeOffset.UtcNow.AddHours(1)));

            Portal = new AccountantPortalService(Firms, Users, Links, Messages, hasher, tokens, Substitute.For<IUnitOfWork>(), TimeProvider.System);
            Desk = new AccountantDeskService(Links, Messages, Firms, new MemoryRepository<Establishment>(Store), Keys, Webhooks, Substitute.For<IUnitOfWork>(), TimeProvider.System);
        }

        public async Task<string> InviteAsync(string email = "contador@escritorio.com", PartnerKind kind = PartnerKind.Accounting) =>
            (await Desk.InviteAsync(Tenant, Store.Id, new InviteAccountantRequest(email, kind), "dono@loja", CancellationToken.None)).Value.Code;

        public AccountantSignUpRequest SignUp(string code, string email = "contador@escritorio.com", string? crc = "MG-123456/O") =>
            new("Escritório Exato", "11444777000161", crc, null, "Maria Contadora", email, "senha-forte-1", code);
    }

    [Fact]
    public async Task Convite_vira_conta_e_a_empresa_aparece_para_o_escritorio()
    {
        var world = new World();
        var code = await world.InviteAsync();

        var session = await world.Portal.SignUpAsync(world.SignUp(code), CancellationToken.None);

        session.IsSuccess.ShouldBeTrue();
        var companies = await world.Portal.ListCompaniesAsync(session.Value.Firm.Id, CancellationToken.None);
        companies.Single().Name.ShouldBe("Açaí do Veloso");
        (await world.Portal.FindLinkAsync(session.Value.Firm.Id, world.Store.Id, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Codigo_e_de_uso_unico_e_so_vale_para_o_email_convidado()
    {
        var world = new World();
        var code = await world.InviteAsync();

        (await world.Portal.SignUpAsync(world.SignUp(code, "outro@escritorio.com"), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await world.Portal.SignUpAsync(world.SignUp(code), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await world.Portal.SignUpAsync(world.SignUp(code, "terceiro@escritorio.com"), CancellationToken.None)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Sem_convite_nao_existe_cadastro()
    {
        var world = new World();

        (await world.Portal.SignUpAsync(world.SignUp("ABCDE-FGHJK"), CancellationToken.None)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Novo_convite_para_o_mesmo_email_anula_o_anterior()
    {
        var world = new World();
        var first = await world.InviteAsync();
        var second = await world.InviteAsync();

        (await world.Portal.SignUpAsync(world.SignUp(first), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await world.Portal.SignUpAsync(world.SignUp(second), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Acesso_revogado_some_do_escritorio()
    {
        var world = new World();
        var session = await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync()), CancellationToken.None);
        var link = (await world.Desk.GetDeskAsync(world.Store.Id, CancellationToken.None)).Links.Single();

        await world.Desk.RevokeAsync(world.Store.Id, link.Id, CancellationToken.None);

        (await world.Portal.FindLinkAsync(session.Value.Firm.Id, world.Store.Id, CancellationToken.None)).ShouldBeNull();
        (await world.Portal.ListCompaniesAsync(session.Value.Firm.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Login_confere_senha_e_trava_depois_de_cinco_erros()
    {
        var world = new World();
        await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync()), CancellationToken.None);

        (await world.Portal.LoginAsync(new AccountantLoginRequest("CONTADOR@escritorio.com", "senha-forte-1"), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        for (var attempt = 0; attempt < AccountantUser.MaxFailedAttempts; attempt++)
        {
            await world.Portal.LoginAsync(new AccountantLoginRequest("contador@escritorio.com", "errada"), CancellationToken.None);
        }

        var locked = await world.Portal.LoginAsync(new AccountantLoginRequest("contador@escritorio.com", "senha-forte-1"), CancellationToken.None);
        locked.Error.Code.ShouldBe("accountant.locked");
    }

    [Fact]
    public async Task Pedido_do_contador_conta_como_pendencia_ate_a_loja_resolver()
    {
        var world = new World();
        var session = await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync()), CancellationToken.None);
        var link = await world.Portal.FindLinkAsync(session.Value.Firm.Id, world.Store.Id, CancellationToken.None);
        var office = new AccountantSide(true, "Maria Contadora");
        var store = new AccountantSide(false, "Dono");

        var request = await world.Desk.PostAsync(world.Store.Id, link!.Id, office, new PostAccountantMessageRequest("Envie o extrato bancário de outubro", IsRequest: true), CancellationToken.None);
        await world.Desk.PostAsync(world.Store.Id, null, store, new PostAccountantMessageRequest("Mando hoje à tarde"), CancellationToken.None);

        var before = (await world.Portal.ListCompaniesAsync(session.Value.Firm.Id, CancellationToken.None)).Single();
        before.OpenRequests.ShouldBe(1);
        before.Unread.ShouldBe(1);

        var seen = await world.Desk.ConversationAsync(world.Store.Id, link.Id, office, CancellationToken.None);
        seen.Count.ShouldBe(2);
        seen[0].Mine.ShouldBeTrue();
        await world.Desk.ResolveAsync(world.Store.Id, null, request.Value.Id, store, CancellationToken.None);

        var after = (await world.Portal.ListCompaniesAsync(session.Value.Firm.Id, CancellationToken.None)).Single();
        after.OpenRequests.ShouldBe(0);
        after.Unread.ShouldBe(0);
    }

    [Fact]
    public async Task Loja_sem_contador_nao_manda_mensagem()
    {
        var world = new World();

        var result = await world.Desk.PostAsync(world.Store.Id, null, new AccountantSide(false, "Dono"), new PostAccountantMessageRequest("Oi"), CancellationToken.None);

        result.Error.Code.ShouldBe("accountant.no_link");
    }

    [Fact]
    public async Task Plataforma_aprova_parceiro_que_pediu()
    {
        var world = new World();
        var session = await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync()), CancellationToken.None);

        (await world.Portal.RequestPartnershipAsync(session.Value.Firm.Id, CancellationToken.None)).Value.PartnerRequestedAt.ShouldNotBeNull();
        var approved = await world.Portal.SetPartnerAsync(session.Value.Firm.Id, new SetPartnerRequest(true), CancellationToken.None);

        approved.Value.IsPartner.ShouldBeTrue();
        (await world.Portal.ListFirmsAsync(CancellationToken.None)).Single().ActiveCompanies.ShouldBe(1);
    }

    [Fact]
    public async Task Software_house_entra_sem_crc_e_contador_nao()
    {
        var world = new World();

        (await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync(), crc: null), CancellationToken.None)).IsFailure.ShouldBeTrue();
        var house = await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync("dev@softhouse.com", PartnerKind.SoftwareHouse), "dev@softhouse.com", crc: null), CancellationToken.None);

        house.IsSuccess.ShouldBeTrue();
        house.Value.Firm.Kind.ShouldBe(PartnerKind.SoftwareHouse);
        (await world.Portal.ListCompaniesAsync(house.Value.Firm.Id, CancellationToken.None)).Single().Kind.ShouldBe(PartnerKind.SoftwareHouse);
    }

    [Fact]
    public async Task Convite_de_software_house_nao_entra_em_conta_de_contador()
    {
        var world = new World();
        var office = await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync()), CancellationToken.None);
        var houseCode = await world.InviteAsync("contador@escritorio.com", PartnerKind.SoftwareHouse);

        var result = await world.Portal.AcceptAsync(office.Value.Firm.Id, "contador@escritorio.com", new AcceptInviteRequest(houseCode), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Tirar_a_software_house_desliga_as_chaves_e_webhooks_dela_e_nao_os_da_loja()
    {
        var world = new World();
        var house = await world.Portal.SignUpAsync(world.SignUp(await world.InviteAsync("dev@softhouse.com", PartnerKind.SoftwareHouse), "dev@softhouse.com", crc: null), CancellationToken.None);
        var firm = house.Value.Firm.Id;
        var partnerKey = new FiscalApiKey(Tenant, world.Store.Id, "ERP", "zf_live_aaaaaaaa", "h1", false, true, "dev", DateTimeOffset.UtcNow, firm);
        var storeKey = new FiscalApiKey(Tenant, world.Store.Id, "PDV", "zf_live_bbbbbbbb", "h2", false, true, "dono", DateTimeOffset.UtcNow);
        var partnerHook = new FiscalWebhook(Tenant, world.Store.Id, "https://erp.exemplo.com", "enc", [FiscalWebhook.NoteAuthorized], DateTimeOffset.UtcNow, firm);
        world.Keys.Add(partnerKey);
        world.Keys.Add(storeKey);
        world.Webhooks.Add(partnerHook);
        var link = (await world.Desk.GetDeskAsync(world.Store.Id, CancellationToken.None)).Links.Single();

        await world.Desk.RevokeAsync(world.Store.Id, link.Id, CancellationToken.None);

        partnerKey.IsActive.ShouldBeFalse();
        storeKey.IsActive.ShouldBeTrue();
        partnerHook.IsActive.ShouldBeFalse();
    }
}
