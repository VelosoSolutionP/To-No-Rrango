namespace ToNoRango.SharedKernel.Abstractions;

/// <summary>
/// Injected instead of DateTimeOffset.UtcNow so that contract, lateness and cost-window rules
/// are deterministically testable.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
