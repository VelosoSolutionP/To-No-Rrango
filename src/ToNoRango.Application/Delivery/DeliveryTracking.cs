using System.Security.Cryptography;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Delivery;

public sealed record GeoPoint(double Latitude, double Longitude);

public sealed record CourierPosition(double Latitude, double Longitude, DateTimeOffset At);

public enum TrackingStage
{
    Left = 1,
    Midway = 2,
    Close = 3,
    Arrived = 4
}

public sealed record TrackingSession(
    string Token,
    Guid OrderId,
    string OrderCode,
    string StoreName,
    string Destination,
    GeoPoint? DestinationPoint,
    double? StartKm = null,
    TrackingStage Stage = TrackingStage.Left);

/// <summary>Short-lived storage: positions expire by themselves and are never written to the database.</summary>
public interface IDeliveryTrackingStore
{
    Task SaveAsync(TrackingSession session);

    Task<TrackingSession?> FindByTokenAsync(string token);

    Task<TrackingSession?> FindByOrderAsync(string orderCode);

    Task SavePositionAsync(string orderCode, CourierPosition position);

    Task<CourierPosition?> FindPositionAsync(string orderCode);

    Task RemoveAsync(TrackingSession session);
}

public interface IGeocoder
{
    /// <summary>Returns null when the address is not found or the service is unavailable.</summary>
    Task<GeoPoint?> FindAsync(string street, string? number, string? city, string? state, CancellationToken cancellationToken);
}

/// <summary>SentToCourier tells the panel the link already went through the store's WhatsApp, so it does not open wa.me.</summary>
public sealed record TrackingLinkDto(string Token, bool SentToCourier = false);

public sealed record CourierPositionRequest(double Latitude, double Longitude);

public sealed record DeliveryIssueRequest(string Reason);

public sealed record CourierSessionDto(bool IsActive, string? OrderCode, string? StoreName, string? Destination, string? PrimaryColor = null, string? SecondaryColor = null);

public sealed record PublicTrackingDto(
    bool IsActive,
    GeoPoint? Courier = null,
    GeoPoint? Destination = null,
    DateTimeOffset? UpdatedAt = null,
    double? DistanceKm = null,
    int? EtaMinutes = null,
    TrackingStage? Stage = null);

public sealed record LiveDeliveryDto(
    Guid OrderId,
    string OrderCode,
    string CustomerName,
    string Destination,
    GeoPoint? Courier,
    GeoPoint? DestinationPoint,
    DateTimeOffset? UpdatedAt,
    double? DistanceKm,
    int? EtaMinutes,
    string? DeliveryIssue = null);

/// <summary>Rough arrival estimate from the straight-line distance. It does not know streets or traffic.</summary>
public static class TrackingEstimate
{
    private const double EarthRadiusKm = 6371;
    private const double StreetFactor = 1.35;
    private const double AverageSpeedKmh = 25;

    private const double ArrivedKm = 0.15;
    private const int CloseMinutes = 2;
    private const double MidwayShare = 0.6;

    public const double MaxPlausibleKm = 60;

    public static double DistanceKm(GeoPoint from, GeoPoint to)
    {
        var latitude = Radians(to.Latitude - from.Latitude);
        var longitude = Radians(to.Longitude - from.Longitude);
        var a = Math.Pow(Math.Sin(latitude / 2), 2)
            + (Math.Cos(Radians(from.Latitude)) * Math.Cos(Radians(to.Latitude)) * Math.Pow(Math.Sin(longitude / 2), 2));

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * StreetFactor;
    }

    public static int EtaMinutes(double distanceKm) => Math.Max(1, (int)Math.Ceiling(distanceKm / AverageSpeedKmh * 60));

    /// <summary>The stage only moves forward, so GPS noise never takes "2 min away" back to "on the way".</summary>
    public static TrackingSession Advance(TrackingSession session, CourierPosition position)
    {
        if (session.DestinationPoint is null)
        {
            return session;
        }

        var distance = DistanceKm(new GeoPoint(position.Latitude, position.Longitude), session.DestinationPoint);

        if (distance > MaxPlausibleKm)
        {
            return session;
        }

        var start = session.StartKm ?? distance;
        var stage = distance <= ArrivedKm ? TrackingStage.Arrived
            : EtaMinutes(distance) <= CloseMinutes ? TrackingStage.Close
            : distance <= start * MidwayShare ? TrackingStage.Midway
            : TrackingStage.Left;

        return session with { StartKm = start, Stage = stage > session.Stage ? stage : session.Stage };
    }

    public static PublicTrackingDto Snapshot(TrackingSession session, CourierPosition? position)
    {
        if (position is null)
        {
            return new PublicTrackingDto(true, Destination: session.DestinationPoint);
        }

        var courier = new GeoPoint(position.Latitude, position.Longitude);

        if (session.DestinationPoint is null)
        {
            return new PublicTrackingDto(true, courier, UpdatedAt: position.At);
        }

        var distance = DistanceKm(courier, session.DestinationPoint);

        // Distância fora do alcance de uma entrega indica endereço localizado errado: melhor não prometer tempo.
        if (distance > MaxPlausibleKm)
        {
            return new PublicTrackingDto(true, courier, UpdatedAt: position.At);
        }

        return new PublicTrackingDto(true, courier, session.DestinationPoint, position.At, Math.Round(distance, 1), EtaMinutes(distance), session.Stage);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

public sealed class DeliveryTrackingService(
    IOrderRepository orders,
    IEstablishmentRepository establishments,
    IDeliveryTrackingStore store,
    IGeocoder geocoder,
    IRepository<ToNoRango.Domain.Delivery.Courier> couriers,
    IRepository<ToNoRango.Domain.Messaging.WhatsAppSettings> whatsAppSettings,
    IRepository<ToNoRango.Domain.Messaging.OutboundMessage> outbox,
    IWhatsAppSender sender,
    IAppUrls urls,
    ToNoRango.Application.Orders.OrderNotifier notifier,
    ToNoRango.SharedKernel.Abstractions.IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const int MaxLiveDeliveries = 100;

    private static readonly PublicTrackingDto Inactive = new(false);
    private static readonly CourierSessionDto Finished = new(false, null, null, null);

    public Task<Result<TrackingLinkDto>> StartAsync(Guid orderId, CancellationToken cancellationToken) =>
        StartAsync(orderId, null, cancellationToken);

    /// <summary>Starts the tracking link. When a courier is given, the order records who is delivering it.</summary>
    public Task<Result<TrackingLinkDto>> StartAsync(Guid orderId, Guid? courierId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<TrackingLinkDto>(async () =>
        {
            var order = await orders.GetByIdAsync(orderId, cancellationToken);

            if (order is null)
            {
                return Result<TrackingLinkDto>.Failure(AppErrors.NotFound("tracking.order_not_found", "Pedido não encontrado."));
            }

            if (order.FulfillmentType != FulfillmentType.Delivery || IsClosed(order))
            {
                return Result<TrackingLinkDto>.Failure(AppErrors.Conflict("tracking.not_available", "O rastreio vale só para pedido de entrega em andamento."));
            }

            ToNoRango.Domain.Delivery.Courier? courier = null;

            if (courierId is Guid chosen)
            {
                courier = await couriers.FindAsync(chosen, cancellationToken);

                if (courier is null || courier.EstablishmentId != order.EstablishmentId)
                {
                    return Result<TrackingLinkDto>.Failure(AppErrors.NotFound("tracking.courier_not_found", "Entregador não encontrado."));
                }

                if (order.CourierId != chosen)
                {
                    order.AssignCourier(courier.Id, courier.Name, clock.GetUtcNow());
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            var existing = await store.FindByOrderAsync(order.Code);

            if (existing is not null)
            {
                return new TrackingLinkDto(existing.Token, courier is not null && await SendToCourierAsync(order, courier, existing.Token, existing.Destination, cancellationToken));
            }

            var establishment = await establishments.GetByIdAsync(order.EstablishmentId, cancellationToken);
            var point = string.IsNullOrWhiteSpace(order.DeliveryStreet)
                ? null
                : await geocoder.FindAsync(order.DeliveryStreet, order.DeliveryNumber, order.DeliveryCity, order.DeliveryState, cancellationToken);
            var destination = string.Join(
                ", ",
                new[] { order.DeliveryStreet, order.DeliveryNumber, order.DeliveryComplement, order.DeliveryDistrict, order.DeliveryCity }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
            var session = new TrackingSession(
                RandomNumberGenerator.GetHexString(32, lowercase: true),
                order.Id,
                order.Code,
                establishment?.Name ?? string.Empty,
                destination,
                point);

            await store.SaveAsync(session);
            return new TrackingLinkDto(session.Token, courier is not null && await SendToCourierAsync(order, courier, session.Token, destination, cancellationToken));
        });

    /// <summary>
    /// Sends the delivery to the courier through the store's own WhatsApp: order code, address and
    /// the link that shares the location. False when the store WhatsApp is not connected.
    /// </summary>
    private async Task<bool> SendToCourierAsync(Order order, ToNoRango.Domain.Delivery.Courier courier, string token, string destination, CancellationToken cancellationToken)
    {
        if (!sender.IsConfigured)
        {
            return false;
        }

        var settings = await whatsAppSettings.FirstOrDefaultAsync(item => item.EstablishmentId == order.EstablishmentId, cancellationToken);

        if (settings is not { IsReady: true })
        {
            return false;
        }

        var reference = string.IsNullOrWhiteSpace(order.DeliveryReference) ? string.Empty : $"\nReferência: {order.DeliveryReference}";
        var method = order.PaymentMethod switch
        {
            PaymentMethod.Cash => "dinheiro",
            PaymentMethod.CardOnDelivery => "cartão na maquininha",
            PaymentMethod.Pix => "Pix",
            _ => "online",
        };
        var amount = (order.TotalInCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        var charge = order.PaymentStatus == PaymentStatus.Paid ? "Já pago." : $"Receber R$ {amount} em {method}.";
        var text = $"🛵 *Entrega {order.Code[^8..]}*\n{order.CustomerName} · {order.CustomerPhone}\n{destination}{reference}\n{charge}\n\nAbra para iniciar e compartilhar sua localização:\n{urls.CourierUrl(token)}";

        outbox.Add(new ToNoRango.Domain.Messaging.OutboundMessage(order.TenantId, order.EstablishmentId, courier.Phone, text, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CourierSessionDto> GetCourierSessionAsync(string token, CancellationToken cancellationToken)
    {
        var session = await store.FindByTokenAsync(token);

        if (session is null || await CloseIfFinishedAsync(session, cancellationToken))
        {
            return Finished;
        }

        var order = await orders.GetByCodeAsync(session.OrderCode, cancellationToken);
        var shop = order is null ? null : await establishments.GetByIdAsync(order.EstablishmentId, cancellationToken);
        return new CourierSessionDto(true, session.OrderCode, session.StoreName, session.Destination, shop?.PrimaryColor, shop?.SecondaryColor);
    }

    public async Task<Result<CourierSessionDto>> ReportPositionAsync(string token, CourierPositionRequest request, CancellationToken cancellationToken)
    {
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
        {
            return Result<CourierSessionDto>.Failure(AppErrors.Validation("Localização inválida."));
        }

        var session = await store.FindByTokenAsync(token);

        if (session is null || await CloseIfFinishedAsync(session, cancellationToken))
        {
            return Finished;
        }

        var position = new CourierPosition(request.Latitude, request.Longitude, clock.GetUtcNow());
        await store.SavePositionAsync(session.OrderCode, position);

        var advanced = TrackingEstimate.Advance(session, position);

        if (advanced != session)
        {
            await store.SaveAsync(advanced);
        }

        return new CourierSessionDto(true, session.OrderCode, session.StoreName, session.Destination);
    }

    /// <summary>
    /// The courier tells the store something went wrong. The store sees it on the order and on the map;
    /// the customer is told the store will get in touch.
    /// </summary>
    public Task<Result<CourierSessionDto>> ReportIssueAsync(string token, DeliveryIssueRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CourierSessionDto>(async () =>
        {
            var session = await store.FindByTokenAsync(token);

            if (session is null || await CloseIfFinishedAsync(session, cancellationToken))
            {
                return Finished;
            }

            var order = await orders.GetByIdAsync(session.OrderId, cancellationToken);

            if (order is null)
            {
                return Finished;
            }

            order.ReportDeliveryIssue(request.Reason, order.CourierName ?? "Entregador", clock.GetUtcNow());
            await notifier.NotifyAsync(
                order,
                $"Pedido {order.Code}: o entregador teve um imprevisto ({order.DeliveryIssue}). A loja já foi avisada e vai falar com você.\nAcompanhe: {notifier.TrackingUrl(order)}",
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new CourierSessionDto(true, session.OrderCode, session.StoreName, session.Destination);
        });

    public Task<Result<ToNoRango.Application.Orders.OrderDto>> ResolveIssueAsync(Guid orderId, string? actor, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ToNoRango.Application.Orders.OrderDto>(async () =>
        {
            var order = await orders.GetByIdAsync(orderId, cancellationToken);

            if (order is null)
            {
                return Result<ToNoRango.Application.Orders.OrderDto>.Failure(AppErrors.NotFound("tracking.order_not_found", "Pedido não encontrado."));
            }

            order.ResolveDeliveryIssue(actor, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToNoRango.Application.Orders.OrderDto.From(order);
        });

    public async Task FinishAsync(string token)
    {
        var session = await store.FindByTokenAsync(token);

        if (session is not null)
        {
            await store.RemoveAsync(session);
        }
    }

    public async Task<PublicTrackingDto> GetPublicAsync(string orderCode, CancellationToken cancellationToken)
    {
        var session = await store.FindByOrderAsync(orderCode);

        if (session is null || await CloseIfFinishedAsync(session, cancellationToken))
        {
            return Inactive;
        }

        return TrackingEstimate.Snapshot(session, await store.FindPositionAsync(orderCode));
    }

    public async Task<IReadOnlyList<LiveDeliveryDto>> ListLiveAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var open = await orders.ListOpenDeliveriesAsync(establishmentId, MaxLiveDeliveries, cancellationToken);
        var live = new List<LiveDeliveryDto>();

        foreach (var order in open)
        {
            var session = await store.FindByOrderAsync(order.Code);

            if (session is null)
            {
                continue;
            }

            var tracking = TrackingEstimate.Snapshot(session, await store.FindPositionAsync(order.Code));
            live.Add(new LiveDeliveryDto(
                order.Id,
                order.Code,
                order.CustomerName,
                session.Destination,
                tracking.Courier,
                tracking.Destination,
                tracking.UpdatedAt,
                tracking.DistanceKm,
                tracking.EtaMinutes,
                order.DeliveryIssue));
        }

        return live;
    }

    private async Task<bool> CloseIfFinishedAsync(TrackingSession session, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(session.OrderId, cancellationToken);

        if (order is not null && !IsClosed(order))
        {
            return false;
        }

        await store.RemoveAsync(session);
        return true;
    }

    private static bool IsClosed(Order order) => order.Status is OrderStatus.Delivered or OrderStatus.Cancelled;
}
