namespace ToNoRango.SharedKernel.Abstractions;

/// <summary>
/// Invariant guards for domain constructors. These protect the aggregate from being built
/// in an impossible state; user facing input is rejected earlier, by the validators.
/// </summary>
public static class Ensure
{
    public static string NotNullOrWhiteSpace(string? value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{paramName} is required.", paramName)
            : value.Trim();

    public static string MaxLength(string value, int maxLength, string paramName)
        => value.Length > maxLength
            ? throw new ArgumentException($"{paramName} exceeds {maxLength} characters.", paramName)
            : value;

    public static Guid NotEmpty(Guid value, string paramName)
        => value == Guid.Empty
            ? throw new ArgumentException($"{paramName} is required.", paramName)
            : value;

    public static decimal NotNegative(decimal value, string paramName)
        => value < 0
            ? throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} cannot be negative.")
            : value;

    public static int NotNegative(int value, string paramName)
        => value < 0
            ? throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} cannot be negative.")
            : value;

    public static int Positive(int value, string paramName)
        => value <= 0
            ? throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be greater than zero.")
            : value;

    public static long NotNegative(long value, string paramName)
        => value < 0
            ? throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} cannot be negative.")
            : value;

    public static T NotNull<T>(T? value, string paramName)
        where T : class
        => value ?? throw new ArgumentNullException(paramName);
}
