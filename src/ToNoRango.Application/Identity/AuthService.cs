using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Contracts;
using ToNoRango.Domain.Identity;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record CreateUserRequest(string Name, string Email, string Password, UserRole Role);

public sealed record SetUserActiveRequest(bool IsActive);

public sealed record UserDto(Guid Id, Guid TenantId, string Name, string Email, UserRole Role, bool IsActive, DateTimeOffset? LastLoginAt)
{
    public static UserDto From(User user) => new(user.Id, user.TenantId, user.Name, user.Email, user.Role, user.IsActive, user.LastLoginAt);
}

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

public sealed class AuthService(
    IRepository<User> users,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
    ContractService contracts,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const int MinimumPasswordLength = 8;

    // Verified when the e-mail is unknown, so both paths cost the same and do not reveal which accounts exist.
    private static string? _noSuchUserHash;

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var invalid = Result<LoginResponse>.Failure(
            Error.Unauthorized("auth.invalid_credentials", "E-mail ou senha incorretos."));

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return invalid;
        }

        var email = User.NormalizeEmail(request.Email);
        var user = await users.FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
        var now = clock.UtcNow;

        if (user is null)
        {
            hasher.Verify(request.Password, _noSuchUserHash ??= hasher.Hash(Guid.NewGuid().ToString()));
            return invalid;
        }

        if (user.IsLocked(now))
        {
            return Result<LoginResponse>.Failure(Error.TooManyRequests(
                "auth.locked",
                "Muitas tentativas incorretas. Aguarde alguns minutos e tente novamente."));
        }

        if (!hasher.Verify(request.Password, user.PasswordHash) || !user.IsActive)
        {
            user.RegisterFailedLogin(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return invalid;
        }

        user.RegisterSuccessfulLogin(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = tokens.Issue(user.Id, user.TenantId, user.Name, user.Email, user.Role.ToString());

        return new LoginResponse(token, expiresAt, UserDto.From(user));
    }

    public async Task<Result<UserDto>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(userId, cancellationToken);

        return user is null
            ? Result<UserDto>.Failure(AppErrors.NotFound("user.not_found", "Usuário não encontrado."))
            : UserDto.From(user);
    }

    public async Task<Result<IReadOnlyList<UserDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var list = await users.ListAsync(user => user.TenantId == tenantId, user => user.Name, cancellationToken);
        return list.Select(UserDto.From).ToList();
    }

    public Task<Result<UserDto>> CreateAsync(Guid tenantId, CreateUserRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<UserDto>(async () =>
        {
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinimumPasswordLength)
            {
                return Result<UserDto>.Failure(AppErrors.Validation($"A senha precisa ter pelo menos {MinimumPasswordLength} caracteres."));
            }

            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            {
                return Result<UserDto>.Failure(AppErrors.Validation("Informe um e-mail válido."));
            }

            var limitError = await contracts.CheckUserLimitAsync(tenantId, cancellationToken);

            if (limitError is not null)
            {
                return Result<UserDto>.Failure(limitError);
            }

            var user = new User(tenantId, request.Name, request.Email, hasher.Hash(request.Password), request.Role, clock.UtcNow);
            users.Add(user);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return UserDto.From(user);
        });

    public Task<Result<UserDto>> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        Guarded.RunAsync<UserDto>(async () =>
        {
            var user = await users.FindAsync(userId, cancellationToken);

            if (user is null)
            {
                return Result<UserDto>.Failure(AppErrors.NotFound("user.not_found", "Usuário não encontrado."));
            }

            if (isActive && !user.IsActive)
            {
                var limitError = await contracts.CheckUserLimitAsync(user.TenantId, cancellationToken);

                if (limitError is not null)
                {
                    return Result<UserDto>.Failure(limitError);
                }
            }

            user.SetActive(isActive);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return UserDto.From(user);
        });
}
