using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Tests.SharedKernel;

public class EnsureTests
{
    [Fact]
    public void NotEmpty_WithEmptyGuid_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ensure.NotEmpty(Guid.Empty, "tenantId"));
    }

    [Fact]
    public void NotEmpty_WithFilledGuid_ReturnsTheValue()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, Ensure.NotEmpty(id, "tenantId"));
    }

    [Fact]
    public void NotNegative_WithNegativeDecimal_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ensure.NotNegative(-0.01m, "price"));
    }

    [Fact]
    public void NotNull_WithNullReference_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Ensure.NotNull<string>(null, "id"));
    }
}
