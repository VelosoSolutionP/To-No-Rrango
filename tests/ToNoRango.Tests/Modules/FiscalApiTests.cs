using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Infrastructure.Integrations;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Tests.Modules;

public class FiscalApiTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();

    private static ApiNoteLine Line(string cfop = "5102", long total = 1000) =>
        new("P1", "Açaí 500ml", 1, total, "21069090", cfop, "00", 18, "01", 0.65m, 3m);

    private static ApiNoteRequest Nfce(string externalId = "venda-1", IReadOnlyList<ApiNotePayment>? payments = null) =>
        new(externalId, 65, [Line()], payments ?? [new ApiNotePayment("pix", 1000)]);

    private static FiscalProfile Profile()
    {
        var profile = new FiscalProfile(Tenant, Store);
        profile.Update("LOJA TESTE LTDA", "Loja", "11444777000161", "0623079040081", "Rua A", "1", "Centro", "Contagem", "MG", "32000000", "3118601", 1, 1);
        return profile;
    }

    // ---------- Validation ----------

    [Fact]
    public void Nfce_valida_passa() => FiscalApiRules.Validate(Nfce()).ShouldBeNull();

    [Fact]
    public void Nfce_sem_pagamento_e_recusada() =>
        FiscalApiRules.Validate(Nfce() with { Payments = [] })!.ShouldContain("payments");

    [Fact]
    public void Pagamento_que_nao_fecha_e_recusado() =>
        FiscalApiRules.Validate(Nfce(payments: [new ApiNotePayment("credit", 900)]))!.ShouldContain("não fecha");

    [Fact]
    public void Dinheiro_acima_do_total_vira_troco() =>
        FiscalApiRules.Validate(Nfce(payments: [new ApiNotePayment("cash", 2000)])).ShouldBeNull();

    [Fact]
    public void Cartao_acima_do_total_e_recusado() =>
        FiscalApiRules.Validate(Nfce(payments: [new ApiNotePayment("credit", 2000)]))!.ShouldContain("não fecha");

    [Fact]
    public void Nfce_nao_aceita_cfop_interestadual() =>
        FiscalApiRules.Validate(Nfce() with { Lines = [Line("6102")] })!.ShouldContain("5xxx");

    [Fact]
    public void Nfe_exige_endereco_do_destinatario()
    {
        var request = new ApiNoteRequest("pedido-9", 55, [Line()], Recipient: new ApiRecipient("11444777000161", "Cliente"));

        FiscalApiRules.Validate(request)!.ShouldContain("endereço");
        FiscalApiRules.Validate(request with { Recipient = new ApiRecipient("11444777000161", "Cliente", Street: "Rua", City: "Contagem", CityIbgeCode: "3118601", State: "MG") })
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("tem espaço")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void Id_externo_invalido_e_recusado(string externalId) =>
        FiscalApiRules.Validate(Nfce(externalId))!.ShouldContain("externalId");

    [Fact]
    public void Devolucao_exige_chave_de_origem() =>
        FiscalApiRules.Validate(new ApiNoteRequest("dev-1", 55, [Line("1202")], Recipient: new ApiRecipient("11444777000161", "F", Street: "R", City: "C", CityIbgeCode: "3118601", State: "MG"), Purpose: 4, IsEntry: true))!
            .ShouldContain("referencedKey");

    // ---------- Identity and keys ----------

    [Fact]
    public void Mesmo_id_externo_cai_na_mesma_nota_e_teste_nao_colide_com_producao()
    {
        FiscalApiRules.DocumentKey(Store, "venda-1", false).ShouldBe(FiscalApiRules.DocumentKey(Store, " venda-1 ", false));
        FiscalApiRules.DocumentKey(Store, "venda-1", true).ShouldNotBe(FiscalApiRules.DocumentKey(Store, "venda-1", false));
        FiscalApiRules.DocumentKey(Guid.NewGuid(), "venda-1", false).ShouldNotBe(FiscalApiRules.DocumentKey(Store, "venda-1", false));
    }

    [Fact]
    public async Task Chave_criada_autentica_so_pelo_hash_e_revogada_para_de_valer()
    {
        var repository = new MemoryRepository<FiscalApiKey>();
        var service = new FiscalApiKeyService(repository, Substitute.For<IUnitOfWork>(), TimeProvider.System);

        var created = await service.CreateAsync(Tenant, Store, new CreateApiKeyRequest("PDV da loja", false, true), "dono@loja", CancellationToken.None);

        created.IsSuccess.ShouldBeTrue();
        created.Value.Secret.ShouldStartWith(FiscalApiKey.LivePrefix);
        var stored = (await repository.ListAsync(_ => true, key => key.CreatedAt, CancellationToken.None)).Single();
        stored.Hash.ShouldNotContain(created.Value.Secret);
        stored.Hash.ShouldBe(FiscalApiRules.HashKey(created.Value.Secret));

        var caller = await service.AuthenticateAsync(created.Value.Secret, CancellationToken.None);
        caller!.EstablishmentId.ShouldBe(Store);
        caller.CanIssue.ShouldBeTrue();
        (await service.AuthenticateAsync(created.Value.Secret + "x", CancellationToken.None)).ShouldBeNull();
        (await service.AuthenticateAsync("Bearer qualquer", CancellationToken.None)).ShouldBeNull();

        await service.RevokeAsync(Store, stored.Id, CancellationToken.None);
        (await service.AuthenticateAsync(created.Value.Secret, CancellationToken.None)).ShouldBeNull();
    }

    // ---------- Simulated notes ----------

    private static (DfeService Service, MemoryRepository<FiscalDocument> Documents, FiscalProfile Profile, IDfeEmitter Emitter) Dfe()
    {
        var profile = Profile();
        var documents = new MemoryRepository<FiscalDocument>();
        var emitter = Substitute.For<IDfeEmitter>();
        var service = new DfeService(
            Substitute.For<IOrderRepository>(),
            new MemoryRepository<FiscalProfile>(profile),
            new MemoryRepository<ProductFiscalData>(),
            documents,
            new MemoryRepository<FiscalNumberGap>(),
            emitter,
            Substitute.For<ISecretProtector>(),
            Substitute.For<IUnitOfWork>(),
            TimeProvider.System);
        return (service, documents, profile, emitter);
    }

    [Fact]
    public async Task Chave_de_teste_simula_sem_sefaz_sem_gastar_numeracao_e_repete_a_mesma_nota()
    {
        var (service, documents, profile, emitter) = Dfe();

        var first = await service.IssueExternalAsync(Store, Nfce(), simulate: true, CancellationToken.None);
        var again = await service.IssueExternalAsync(Store, Nfce(), simulate: true, CancellationToken.None);
        var other = await service.IssueExternalAsync(Store, Nfce("venda-2"), simulate: true, CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Status.ShouldBe(FiscalDocumentStatus.Authorized);
        first.Value.Environment.ShouldBe(2);
        first.Value.Series.ShouldBe(FiscalApiRules.SimulationSeries);
        first.Value.Protocol.ShouldBe(FiscalDocument.SimulatedProtocol);
        first.Value.AccessKey!.Length.ShouldBe(44);
        FiscalApiRules.CheckDigit(first.Value.AccessKey[..43]).ShouldBe(first.Value.AccessKey[43] - '0');
        again.Value.Id.ShouldBe(first.Value.Id);
        other.Value.Number.ShouldBe(2);
        profile.NextNumber.ShouldBe(1);
        await emitter.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, TestContext.Current.CancellationToken);
        (await documents.CountAsync(_ => true, CancellationToken.None)).ShouldBe(2);
    }

    [Fact]
    public async Task Nota_simulada_cancela_sem_sefaz()
    {
        var (service, _, _, emitter) = Dfe();
        var note = await service.IssueExternalAsync(Store, Nfce(), simulate: true, CancellationToken.None);

        var cancelled = await service.CancelAsync(Store, note.Value.Id, new CancelNoteRequest("Cliente desistiu da compra no caixa"), CancellationToken.None);

        cancelled.Value.Status.ShouldBe(FiscalDocumentStatus.Cancelled);
        await emitter.DidNotReceiveWithAnyArgs().CancelAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Nota_simulada_fica_fora_da_apuracao()
    {
        var (service, documents, profile, _) = Dfe();
        await service.IssueExternalAsync(Store, Nfce(), simulate: true, CancellationToken.None);
        var source = new FiscalNoteSource(new MemoryRepository<ToNoRango.Domain.Inventory.PurchaseEntry>(), documents);
        var warnings = new List<string>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var notes = await source.LoadAsync(Store, today.AddDays(-1), today.AddDays(1), warnings, CancellationToken.None);

        notes.ShouldBeEmpty();
        warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Chave_so_de_consulta_nao_emite()
    {
        var (dfe, documents, _, _) = Dfe();
        var api = new FiscalApiService(documents, dfe);
        var caller = new FiscalApiCaller(Guid.NewGuid(), Tenant, Store, "ERP", "zf_test_abc", true, CanIssue: false);

        var result = await api.IssueAsync(caller, Nfce(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("fiscal_api.read_only");
    }

    [Fact]
    public async Task Chave_de_teste_nao_enxerga_nota_real()
    {
        var (dfe, documents, _, _) = Dfe();
        var real = new FiscalDocument(Tenant, Store, Guid.NewGuid(), "P1", 1, 7, 1, 1000, DateTimeOffset.UtcNow);
        documents.Add(real);
        var api = new FiscalApiService(documents, dfe);
        var test = new FiscalApiCaller(Guid.NewGuid(), Tenant, Store, "ERP", "zf_test_abc", true, true);

        (await api.GetAsync(test, real.Id, CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await api.ListAsync(test, null, null, null, null, CancellationToken.None)).ShouldBeEmpty();
    }

    // ---------- Webhooks ----------

    private sealed class RecordingSender(bool accept = true) : IWebhookSender
    {
        public List<(string Event, string Body, string Signature, long Timestamp)> Sent { get; } = [];

        public string? Validate(string url) => url.StartsWith("https://", StringComparison.Ordinal) ? null : "O webhook precisa usar https.";

        public Task<WebhookSendResult> SendAsync(string url, string eventName, Guid deliveryId, string body, string signature, long timestamp, CancellationToken cancellationToken)
        {
            Sent.Add((eventName, body, signature, timestamp));
            return Task.FromResult(accept ? new WebhookSendResult(true, 200, null) : new WebhookSendResult(false, 500, "Resposta 500."));
        }
    }

    private static (FiscalWebhookService Service, MemoryRepository<FiscalWebhookDelivery> Deliveries, MemoryRepository<FiscalDocument> Documents) Webhooks(RecordingSender sender)
    {
        var protector = Substitute.For<ISecretProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        protector.Unprotect(Arg.Any<string>()).Returns(call => call.Arg<string>()[4..]);
        var deliveries = new MemoryRepository<FiscalWebhookDelivery>();
        var documents = new MemoryRepository<FiscalDocument>();
        var keys = new FiscalApiKeyService(new MemoryRepository<FiscalApiKey>(), Substitute.For<IUnitOfWork>(), TimeProvider.System);
        var service = new FiscalWebhookService(new MemoryRepository<FiscalWebhook>(), deliveries, documents, new MemoryRepository<AccountantFirm>(), keys, sender, protector, Substitute.For<IUnitOfWork>(), TimeProvider.System);
        return (service, deliveries, documents);
    }

    [Fact]
    public async Task Webhook_recebe_nota_autorizada_uma_vez_com_assinatura_conferivel()
    {
        var sender = new RecordingSender();
        var (service, deliveries, documents) = Webhooks(sender);
        var created = await service.CreateAsync(Tenant, Store, new CreateWebhookRequest("https://erp.exemplo.com/zest", [FiscalWebhook.NoteAuthorized]), CancellationToken.None);
        created.Value.Secret.ShouldStartWith("whsec_");

        var note = new FiscalDocument(Tenant, Store, Guid.NewGuid(), "P1", 1, 7, 1, 1000, DateTimeOffset.UtcNow.AddSeconds(1));
        note.Authorize(new string('3', 44), "131260000000001", "<xml/>", "ok", DateTimeOffset.UtcNow.AddSeconds(1));
        documents.Add(note);

        (await service.ScanAsync(CancellationToken.None)).ShouldBe(1);
        (await service.ScanAsync(CancellationToken.None)).ShouldBe(0);
        (await service.DeliverDueAsync(CancellationToken.None)).ShouldBe(1);

        var (eventName, body, signature, timestamp) = sender.Sent.Single();
        eventName.ShouldBe(FiscalWebhook.NoteAuthorized);
        signature.ShouldBe(FiscalApiRules.Sign(created.Value.Secret, timestamp, body));
        JsonDocument.Parse(body).RootElement.GetProperty("note").GetProperty("accessKey").GetString().ShouldBe(new string('3', 44));
        (await deliveries.ListAsync(_ => true, item => item.CreatedAt, CancellationToken.None)).Single().Status.ShouldBe(WebhookDeliveryStatus.Delivered);
    }

    [Fact]
    public void Nota_autorizada_e_cancelada_entre_duas_leituras_gera_os_dois_avisos_em_ordem()
    {
        var note = new FiscalDocument(Tenant, Store, Guid.NewGuid(), "P1", 1, 7, 1, 1000, DateTimeOffset.UtcNow);
        note.Authorize(new string('3', 44), "1", null, "ok", DateTimeOffset.UtcNow);
        note.Cancel("2", "Cliente desistiu da compra no caixa", null, DateTimeOffset.UtcNow);

        FiscalWebhookService.EventsOf(note).Select(item => item.Name).ShouldBe([FiscalWebhook.NoteAuthorized, FiscalWebhook.NoteCancelled]);
    }

    [Fact]
    public async Task Evento_nao_assinado_nao_e_enviado()
    {
        var (service, _, documents) = Webhooks(new RecordingSender());
        await service.CreateAsync(Tenant, Store, new CreateWebhookRequest("https://erp.exemplo.com/zest", [FiscalWebhook.NoteCancelled]), CancellationToken.None);
        var note = new FiscalDocument(Tenant, Store, Guid.NewGuid(), "P1", 1, 7, 1, 1000, DateTimeOffset.UtcNow.AddSeconds(1));
        note.Authorize(new string('3', 44), "1", null, "ok", DateTimeOffset.UtcNow.AddSeconds(1));
        documents.Add(note);

        (await service.ScanAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task Falha_no_envio_agenda_nova_tentativa_e_desiste_depois_de_todas()
    {
        var (service, deliveries, _) = Webhooks(new RecordingSender(accept: false));
        var hook = await service.CreateAsync(Tenant, Store, new CreateWebhookRequest("https://erp.exemplo.com/zest", [FiscalWebhook.NoteAuthorized]), CancellationToken.None);
        await service.TestAsync(Store, hook.Value.Webhook.Id, CancellationToken.None);

        await service.DeliverDueAsync(CancellationToken.None);

        var delivery = (await deliveries.ListAsync(_ => true, item => item.CreatedAt, CancellationToken.None)).Single();
        delivery.Status.ShouldBe(WebhookDeliveryStatus.Pending);
        delivery.Attempts.ShouldBe(1);
        delivery.NextAttemptAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(50));

        for (var attempt = 0; attempt < FiscalWebhookDelivery.Backoff.Length; attempt++)
        {
            delivery.Fail(500, "Resposta 500.", DateTimeOffset.UtcNow);
        }

        delivery.Status.ShouldBe(WebhookDeliveryStatus.Failed);
    }

    [Fact]
    public async Task Webhook_sem_https_e_recusado()
    {
        var (service, _, _) = Webhooks(new RecordingSender());

        var result = await service.CreateAsync(Tenant, Store, new CreateWebhookRequest("http://erp.exemplo.com", [FiscalWebhook.NoteAuthorized]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://192.168.1.10/x")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://[::1]/x")]
    [InlineData("http://erp.exemplo.com/x")]
    [InlineData("https://user:pass@erp.exemplo.com/x")]
    public void Endereco_interno_ou_inseguro_e_bloqueado(string url) =>
        new FiscalWebhookSender(new HttpClient(), new FiscalApiOptions(), NullLogger<FiscalWebhookSender>.Instance).Validate(url).ShouldNotBeNull();

    [Fact]
    public void Endereco_publico_passa()
    {
        new FiscalWebhookSender(new HttpClient(), new FiscalApiOptions(), NullLogger<FiscalWebhookSender>.Instance).Validate("https://erp.exemplo.com/zest").ShouldBeNull();
        FiscalWebhookSender.IsPublic(IPAddress.Parse("8.8.8.8")).ShouldBeTrue();
        FiscalWebhookSender.IsPublic(IPAddress.Parse("172.20.0.3")).ShouldBeFalse();
        FiscalWebhookSender.IsPublic(IPAddress.Parse("::ffff:10.0.0.1")).ShouldBeFalse();
    }

    [Fact]
    public async Task Software_house_ve_e_revoga_so_as_proprias_chaves()
    {
        var repository = new MemoryRepository<FiscalApiKey>();
        var service = new FiscalApiKeyService(repository, Substitute.For<IUnitOfWork>(), TimeProvider.System);
        var partner = Guid.NewGuid();
        await service.CreateAsync(Tenant, Store, new CreateApiKeyRequest("Da loja", true, true), "dono", CancellationToken.None);
        var mine = await service.CreateAsync(Tenant, Store, new CreateApiKeyRequest("Do parceiro", true, true), "dev", CancellationToken.None, partner);
        var storeKey = (await service.ListAsync(Store, CancellationToken.None)).Single(key => key.PartnerFirmId == null);

        (await service.ListAsync(Store, CancellationToken.None, partner)).Single().Id.ShouldBe(mine.Value.Key.Id);
        (await service.RevokeAsync(Store, storeKey.Id, CancellationToken.None, partner)).IsFailure.ShouldBeTrue();
        (await service.RevokeAsync(Store, mine.Value.Key.Id, CancellationToken.None, partner)).IsSuccess.ShouldBeTrue();
        (await service.RevokeAsync(Store, storeKey.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }
}
