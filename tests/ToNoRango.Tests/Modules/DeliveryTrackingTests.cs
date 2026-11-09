using ToNoRango.Application.Delivery;
using ToNoRango.Domain.Delivery;

namespace ToNoRango.Tests.Modules;

public class DeliveryTrackingTests
{
    [Fact]
    public void Distance_includes_the_street_factor()
    {
        var distance = TrackingEstimate.DistanceKm(new GeoPoint(-19.9320, -44.0539), new GeoPoint(-19.9320, -44.0239));
        Assert.InRange(distance, 4.0, 4.6);
    }

    [Fact]
    public void Same_place_never_shows_zero_minutes() => Assert.Equal(1, TrackingEstimate.EtaMinutes(0));

    [Fact]
    public void Eta_uses_the_average_speed() => Assert.Equal(12, TrackingEstimate.EtaMinutes(5));

    private static readonly GeoPoint Home = new(-19.9320, -44.0239);

    private static TrackingSession Session(GeoPoint? destination) => new("token", Guid.NewGuid(), "TR-1", "Loja", "Rua A", destination);

    [Fact]
    public void Snapshot_without_position_is_waiting_for_the_courier()
    {
        var snapshot = TrackingEstimate.Snapshot(Session(Home), null);

        Assert.True(snapshot.IsActive);
        Assert.Null(snapshot.Courier);
        Assert.Equal(Home, snapshot.Destination);
    }

    [Fact]
    public void Snapshot_estimates_arrival_inside_the_delivery_range()
    {
        var snapshot = TrackingEstimate.Snapshot(Session(Home), new CourierPosition(-19.9320, -44.0539, DateTimeOffset.UtcNow));

        Assert.Equal(Home, snapshot.Destination);
        Assert.NotNull(snapshot.DistanceKm);
        Assert.NotNull(snapshot.EtaMinutes);
    }

    [Fact]
    public void Snapshot_hides_the_estimate_when_the_destination_is_implausibly_far()
    {
        var snapshot = TrackingEstimate.Snapshot(Session(new GeoPoint(-23.55, -46.63)), new CourierPosition(-19.9320, -44.0539, DateTimeOffset.UtcNow));

        Assert.NotNull(snapshot.Courier);
        Assert.Null(snapshot.Destination);
        Assert.Null(snapshot.EtaMinutes);
    }

    [Theory]
    [InlineData("(31) 99999-8888")]
    [InlineData("31999998888")]
    [InlineData("+55 31 99999-8888")]
    public void Courier_phone_is_stored_in_whatsapp_form(string phone)
    {
        var courier = new Courier(Guid.NewGuid(), Guid.NewGuid(), " João ", phone);

        Assert.Equal("5531999998888", courier.Phone);
        Assert.Equal("João", courier.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("99998888")]
    [InlineData("1 202 555 0100 00")]
    public void Courier_phone_needs_a_brazilian_area_code(string phone) =>
        Assert.Throws<ArgumentException>(() => new Courier(Guid.NewGuid(), Guid.NewGuid(), "João", phone));
}
