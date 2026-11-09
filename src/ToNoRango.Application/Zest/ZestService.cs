using System.Text;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Orders;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Delivery;
using ToNoRango.Domain.Establishments;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Orders;
using ToNoRango.Domain.Payments;
using ToNoRango.Domain.Zest;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Zest;

public sealed record KnowledgeRequest(string Topic, string Answer, string? Keywords, bool IsActive = true);

public sealed record KnowledgeDto(Guid Id, string Topic, string Answer, string Keywords, bool IsActive)
{
    public static KnowledgeDto From(KnowledgeEntry entry) => new(entry.Id, entry.Topic, entry.Answer, entry.Keywords, entry.IsActive);
}

public sealed record ZestAskRequest(string Message);

public sealed record ZestAnswerDto(ZestIntent Intent, string Text);

public sealed class ZestService(
    IRepository<KnowledgeEntry> knowledge,
    IRepository<DeliveryZone> zones,
    IRepository<PaymentSettings> paymentSettings,
    IRepository<Order> orders,
    IMenuRepository menu,
    IEstablishmentRepository establishments,
    IAppUrls urls,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<IReadOnlyList<KnowledgeDto>>> ListKnowledgeAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await knowledge.ListAsync(entry => entry.EstablishmentId == establishmentId, entry => entry.Topic, cancellationToken);
        return list.Select(KnowledgeDto.From).ToList();
    }

    public Task<Result<KnowledgeDto>> CreateKnowledgeAsync(Guid establishmentId, KnowledgeRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<KnowledgeDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<KnowledgeDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var entry = new KnowledgeEntry(establishment.TenantId, establishment.Id, request.Topic, request.Answer, request.Keywords ?? string.Empty, clock.UtcNow);
            knowledge.Add(entry);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return KnowledgeDto.From(entry);
        });

    public Task<Result<KnowledgeDto>> UpdateKnowledgeAsync(Guid entryId, KnowledgeRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<KnowledgeDto>(async () =>
        {
            var entry = await knowledge.FindAsync(entryId, cancellationToken);

            if (entry is null)
            {
                return Result<KnowledgeDto>.Failure(AppErrors.NotFound("knowledge.not_found", "Conhecimento não encontrado."));
            }

            entry.Update(request.Topic, request.Answer, request.Keywords ?? string.Empty, request.IsActive, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return KnowledgeDto.From(entry);
        });

    public Task<Result<bool>> DeleteKnowledgeAsync(Guid entryId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<bool>(async () =>
        {
            var entry = await knowledge.FindAsync(entryId, cancellationToken);

            if (entry is null)
            {
                return Result<bool>.Failure(AppErrors.NotFound("knowledge.not_found", "Conhecimento não encontrado."));
            }

            knowledge.Remove(entry);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        });

    public async Task<Result<ZestAnswerDto>> AskAsync(Guid establishmentId, ZestAskRequest request, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<ZestAnswerDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Result<ZestAnswerDto>.Failure(AppErrors.Validation("Escreva uma mensagem."));
        }

        return await AnswerAsync(establishment, null, request.Message, cancellationToken);
    }

    /// <summary>Answers from the store's own data. The AI runs inside ZEST, never here.</summary>
    public async Task<ZestAnswerDto> AnswerAsync(Establishment establishment, string? phone, string message, CancellationToken cancellationToken)
    {
        var reply = ZestBrain.Answer(await BuildContextAsync(establishment, phone, cancellationToken), message);

        return new ZestAnswerDto(reply.Intent, reply.Text);
    }

    private async Task<ZestContext> BuildContextAsync(Establishment establishment, string? phone, CancellationToken cancellationToken)
    {
        var categories = await menu.ListCategoriesAsync(establishment.Id, false, cancellationToken);
        var names = categories.ToDictionary(category => category.Id, category => category.Name);
        var items = await menu.ListItemsAsync(establishment.Id, false, cancellationToken);
        var entries = await knowledge.ListAsync(entry => entry.EstablishmentId == establishment.Id && entry.IsActive, entry => entry.Topic, cancellationToken);
        var activeZones = await zones.ListAsync(zone => zone.EstablishmentId == establishment.Id && zone.IsActive, zone => zone.District, cancellationToken);
        var payment = await paymentSettings.FirstOrDefaultAsync(item => item.EstablishmentId == establishment.Id, cancellationToken);

        ZestOrder? lastOrder = null;

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var key = PhoneNumber.Key(phone);
            var recent = await orders.ListAsync(
                order => order.EstablishmentId == establishment.Id && order.CustomerPhoneKey == key,
                order => order.CreatedAt,
                cancellationToken,
                descending: true,
                take: 1);

            if (recent.Count > 0)
            {
                lastOrder = new ZestOrder(recent[0].Code, OrderMessages.StatusLabel(recent[0].Status), urls.TrackingUrl(recent[0].Code));
            }
        }

        var methods = payment is { CanChargeCard: true }
            ? "Pix, dinheiro, cartão na entrega e cartão online"
            : "Pix, dinheiro e cartão na entrega";

        return new ZestContext(
            establishment.Name,
            urls.MenuUrl(establishment.Slug),
            establishment.AddressLine,
            establishment.IsDeliveryEnabled,
            establishment.IsPickupEnabled,
            establishment.DeliveryFeeInCents,
            establishment.MinimumOrderInCents,
            methods,
            activeZones.Select(zone => new ZestZone(zone.District, zone.FeeInCents)).ToList(),
            items.Select(item => new ZestMenuItem(item.Name, item.PriceInCents, names.GetValueOrDefault(item.CategoryId, "Cardápio"), item.IsAvailable, item.Description)).ToList(),
            entries.Select(entry => new ZestKnowledge(entry.Topic, entry.Answer, entry.Keywords)).ToList(),
            lastOrder);
    }
}
