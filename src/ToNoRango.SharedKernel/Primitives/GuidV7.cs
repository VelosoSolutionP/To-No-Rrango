namespace ToNoRango.SharedKernel.Primitives;

/// <summary>
/// Time-ordered identifiers. Random Guid v4 primary keys fragment PostgreSQL B-tree indexes badly
/// on high-insert tables such as orders and audit entries; v7 keeps inserts on the right edge of the
/// index and keeps page splits cheap.
/// </summary>
public static class GuidV7
{
    public static Guid New() => Guid.CreateVersion7();

    public static Guid New(DateTimeOffset timestamp) => Guid.CreateVersion7(timestamp);
}
