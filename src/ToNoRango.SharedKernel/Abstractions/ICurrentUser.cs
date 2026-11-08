namespace ToNoRango.SharedKernel.Abstractions;

/// <summary>
/// The authenticated principal for the current request. Backed by the JWT claims.
/// Everything that writes audit rows or applies the tenant filter reads from here.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    Guid? CustomerId { get; }

    Guid? TenantId { get; }

    string? Email { get; }

    string? DisplayName { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? TraceId { get; }

    bool HasPermission(string permission);

    bool IsInRole(string role);
}
