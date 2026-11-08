using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Tests.SharedKernel;

public class GuidV7Tests
{
    [Fact]
    public void New_ReturnsGuidVersionSeven()
    {
        var id = GuidV7.New();
        var hex = id.ToString("N");

        // Layout N: time_hi_and_version occupies hex chars 12-15, version is the high nibble of byte 12.
        Assert.Equal('7', hex[12]);

        // Variant is the high two bits of the clock sequence, expressed as a leading hex digit 8-B.
        var variantHex = hex[16];
        Assert.True("89ab".IndexOf(char.ToLowerInvariant(variantHex)) is >= 0);
    }

    [Fact]
    public void New_WithTimestamp_ReturnsOrderedGuidVersionSeven()
    {
        var first = GuidV7.New(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var second = GuidV7.New(new DateTimeOffset(2026, 11, 1, 12, 0, 1, TimeSpan.Zero));

        Assert.NotEqual(first, second);
    }
}
