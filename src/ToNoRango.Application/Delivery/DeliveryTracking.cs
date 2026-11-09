using System.Security.Cryptography;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Delivery;

public sealed record GeoPoint(double Latitude, double Longitude);

public sealed record CourierPosition(double Latitude, double Longitude, DateTimeOffset At);

public sealed record TrackingSession(string Token, Guid OrderId, string OrderCode, string StoreName, string Destination, GeoPoint? DestinationPoint);

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

public sealed record TrackingLinkDto(string Token);

public sealed record CourierPositionRequest(double Latitude, double Longitude);

public sealed record CourierSessionDto(bool IsActive, string? OrderCode, string? StoreName, string? Destination);

public sealed record PublicTrackingDto(
    bool IsActive,
    GeoPoint? Courier = null,
    GeoPoint? Destination = null,
    DateTimeOffset? UpdatedAt = null,
    double? DistanceKm = null,
    int? EtaMinutes = null);

public sealed record LiveDeliveryDto(
    Guid OrderId,
    string OrderCode,
    string CustomerName,
    string Destination,
    GeoPoint? Courier,
    GeoPoint? DestinationPoint,
    DateTimeOffset? UpdatedAt,
    double? DistanceKm,
    int? EtaMinutes);

/// <summary>Rough arrival estimate from the straight-line distance. It does not know streets or traffic.</summary>
public static class TrackingEstimate
{
    private const double EarthRadiusKm = 6371;
    private const double StreetFactor = 1.35;
    private const double AverageSpeedKmh = 25;

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

        return new PublicTrackingDto(true, courier, session.DestinationPoint, position.At, Math.Round(distance, 1), EtaMinutes(distance));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

public sealed class DeliveryTrackingService(
    IOrderRepository orders,
    IEstablishmentRepository establishments,
    IDeliveryTrackingStore store,
    IGeocoder geocoder,
    TimeProvider clock)
{
    private const int MaxLiveDeliveries = 100;

    private static readonly PublicTrackingDto Inactive = new(false);
    private static readonly CourierSessionDto Finished = new(false, null, null, null);

    public Task<Result<TrackingLinkDto>> StartAsync(Guid orderId, CancellationToken cancellationToken) =>
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

            var existing = await store.FindByOrderAsync(order.Code);

            if (existing is not null)
            {
                return new TrackingLinkDto(existing.Token);
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
            return new TrackingLinkDto(session.Token);
        });

    public async Task<CourierSessionDto> GetCourierSessionAsync(string token, CancellationToken cancellationToken)
    {
        var session = await store.FindByTokenAsync(token);

        if (session is null || await CloseIfFinishedAsync(session, cancellationToken))
        {
            return Finished;
        }

        return new CourierSessionDto(true, session.OrderCode, session.StoreName, session.Destination);
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

        await store.SavePositionAsync(session.OrderCode, new CourierPosition(request.Latitude, request.Longitude, clock.GetUtcNow()));
        return new CourierSessionDto(true, session.OrderCode, session.StoreName, session.Destination);
    }

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
                tracking.EtaMinutes));
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
