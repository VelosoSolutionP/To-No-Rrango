using ToNoRango.Application.Fiscal;

namespace ToNoRango.Api.Endpoints;

/// <summary>NFS-e (padrão nacional): in the panel and in the fiscal API, with the same service behind.</summary>
internal static class NfseEndpoints
{
    private const string Establishment = "/establishments/{establishmentId:guid}";

    public static void MapPanel(RouteGroupBuilder group)
    {
        group.MapGet($"{Establishment}/fiscal/nfse/settings", async (Guid establishmentId, NfseService service, CancellationToken ct) =>
            (await service.GetSettingsAsync(establishmentId, ct)).ToHttpResult());

        group.MapPut($"{Establishment}/fiscal/nfse/settings", async (Guid establishmentId, NfseSettingsRequest request, NfseService service, CancellationToken ct) =>
            (await service.SaveSettingsAsync(establishmentId, request, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/fiscal/nfse", async (Guid establishmentId, NfseService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(establishmentId, false, ct)));

        group.MapPost($"{Establishment}/fiscal/nfse", async (Guid establishmentId, ServiceNoteRequest request, NfseService service, CancellationToken ct) =>
            (await service.IssueAsync(establishmentId, request, false, ct)).ToHttpResult());

        group.MapPost($"{Establishment}/fiscal/nfse/{{noteId:guid}}/cancel", async (Guid establishmentId, Guid noteId, CancelServiceNoteRequest request, NfseService service, CancellationToken ct) =>
            (await service.CancelAsync(establishmentId, noteId, request, false, ct)).ToHttpResult());

        group.MapGet($"{Establishment}/fiscal/nfse/{{noteId:guid}}/xml", async (Guid establishmentId, Guid noteId, NfseService service, CancellationToken ct) =>
            (await service.GetXmlAsync(establishmentId, noteId, false, ct)).ToHttpResult());
    }

    public static void MapApi(RouteGroupBuilder api)
    {
        api.MapGet("/service-notes", async (HttpContext http, int? take, NfseService service, CancellationToken ct) =>
        {
            var caller = http.FiscalCaller()!;
            return Results.Ok(await service.ListAsync(caller.EstablishmentId, caller.IsTest, ct, take ?? 50));
        });

        api.MapPost("/service-notes", async (HttpContext http, ServiceNoteRequest request, NfseService service, CancellationToken ct) =>
        {
            var caller = http.FiscalCaller()!;

            if (!caller.CanIssue)
            {
                return Results.Problem(title: "fiscal_api.read_only", detail: "Esta chave só consulta.", statusCode: StatusCodes.Status403Forbidden);
            }

            var issued = await service.IssueAsync(caller.EstablishmentId, request, caller.IsTest, ct);
            return issued.IsSuccess ? Results.Created($"/api/fiscal/v1/service-notes/{issued.Value.Id}", issued.Value) : issued.ToHttpResult();
        });

        api.MapGet("/service-notes/{noteId:guid}", async (HttpContext http, Guid noteId, NfseService service, CancellationToken ct) =>
            (await service.GetAsync(http.FiscalCaller()!.EstablishmentId, noteId, http.FiscalCaller()!.IsTest, ct)).ToHttpResult());

        api.MapGet("/service-notes/{noteId:guid}/xml", async (HttpContext http, Guid noteId, NfseService service, CancellationToken ct) =>
        {
            var xml = await service.GetXmlAsync(http.FiscalCaller()!.EstablishmentId, noteId, http.FiscalCaller()!.IsTest, ct);
            return xml.IsSuccess ? Results.Text(xml.Value, "application/xml; charset=utf-8") : xml.ToHttpResult();
        });

        api.MapPost("/service-notes/{noteId:guid}/cancel", async (HttpContext http, Guid noteId, CancelServiceNoteRequest request, NfseService service, CancellationToken ct) =>
        {
            var caller = http.FiscalCaller()!;
            return caller.CanIssue
                ? (await service.CancelAsync(caller.EstablishmentId, noteId, request, caller.IsTest, ct)).ToHttpResult()
                : Results.Problem(title: "fiscal_api.read_only", detail: "Esta chave só consulta.", statusCode: StatusCodes.Status403Forbidden);
        });
    }
}
