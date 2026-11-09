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
