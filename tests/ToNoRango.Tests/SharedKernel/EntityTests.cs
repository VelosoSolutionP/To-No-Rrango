using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Tests.SharedKernel;

public sealed class FakeEntity : Entity
{
    public FakeEntity(Guid id) : base(id) { }
}

public sealed class OtherEntity : Entity
{
    public OtherEntity(Guid id) : base(id) { }
}

public class EntityTests
{
    [Fact]
    public void EqualIdentityInSameType_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var left = new FakeEntity(id);
        var right = new FakeEntity(id);

        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void DifferentTypeWithSameId_ReturnsFalse()
    {
        var entity = new FakeEntity(Guid.NewGuid());

        Assert.False(entity.Equals(new OtherEntity(entity.Id)));
    }

    [Fact]
    public void DifferentId_ReturnsFalse()
    {
        var left = new FakeEntity(Guid.NewGuid());
        var right = new FakeEntity(Guid.NewGuid());

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void GuidEmptyIdentity_ReturnsFalse()
    {
        var entity = new FakeEntity(Guid.Empty);
        var other = new FakeEntity(Guid.Empty);

        Assert.False(entity.Equals(other));
    }
}
