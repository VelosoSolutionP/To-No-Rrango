using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Identity;

public enum UserRole
{
    Owner = 1,
    Manager = 2,
    Attendant = 3,
    Kitchen = 4
}

public sealed class User : AggregateRoot
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public User(Guid tenantId, string name, string email, string passwordHash, UserRole role, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Email = NormalizeEmail(email);
        PasswordHash = Ensure.NotNullOrWhiteSpace(passwordHash, nameof(PasswordHash));
        Role = role;
        CreatedAt = now;
    }

    public static string NormalizeEmail(string email) =>
        Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(email, nameof(Email)), 200, nameof(Email)).ToLowerInvariant();

    public bool IsLocked(DateTimeOffset now) => LockedUntil is not null && LockedUntil > now;

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedAttempts)
        {
            LockedUntil = now.Add(LockoutDuration);
            FailedLoginAttempts = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void ChangePassword(string passwordHash) =>
        PasswordHash = Ensure.NotNullOrWhiteSpace(passwordHash, nameof(PasswordHash));
}
