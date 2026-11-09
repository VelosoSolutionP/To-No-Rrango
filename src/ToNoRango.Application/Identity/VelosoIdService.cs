using System.Security.Cryptography;
using System.Text;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Identity;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Identity;

/// <summary>Where the Portal is and how this application presents itself. Empty: the Veloso ID is off.</summary>
public sealed class VelosoIdOptions
{
    public const string SectionName = "VelosoId";

    public string PortalWebUrl { get; set; } = string.Empty;
    public string PortalApiUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = "TO-NO-RANGO";
    public string RootSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>The old e-mail and password form. Only turned off after the customers have been migrated.</summary>
    public bool PasswordLoginEnabled { get; set; } = true;

    /// <summary>Customers enter through the Portal only; the owner of the platform keeps a password, as in ZEST.</summary>
    public string OwnerEmail { get; set; } = string.Empty;

    /// <summary>How often an open session is confirmed with the Portal.</summary>
    public int CheckSeconds { get; set; } = 300;

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(PortalWebUrl) && !string.IsNullOrWhiteSpace(PortalApiUrl) &&
        !string.IsNullOrWhiteSpace(RootSecret) && !string.IsNullOrWhiteSpace(RedirectUri);

    public bool AllowsPassword(string? email) =>
        !Enabled || PasswordLoginEnabled
        || (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(OwnerEmail) && User.NormalizeEmail(email) == User.NormalizeEmail(OwnerEmail));
}

public sealed record VelosoPerson(Guid Id, string Name, string Email);

public sealed record VelosoCompany(Guid Id, string? Document, string Name);

public sealed record VelosoAuthorization(string SessionHandle, DateTimeOffset SessionExpiresAt, int CheckAfterSeconds, VelosoPerson Person, VelosoCompany Company, string App, IReadOnlyList<string> Modules);

public sealed record VelosoSessionState(bool Active, string? Reason, string? Message, int CheckAfterSeconds);

/// <summary>The server to server half of the Veloso ID. Implemented over HTTP.</summary>
public interface IVelosoIdGateway
{
    Task<VelosoAuthorization?> RedeemAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken);

    Task<VelosoSessionState> CheckAsync(string sessionHandle, CancellationToken cancellationToken);

    Task EndAsync(string sessionHandle, CancellationToken cancellationToken);
}

/// <summary>
/// Same derivation in every application: the credential changes at midnight in Brasilia with nobody
/// doing anything, the state carries no secret and the PKCE verifier can only be rebuilt here.
/// </summary>
public static class VelosoIdProtocol
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static string DailyCredential(string root, string clientId, DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Brasilia).DateTime);
        return Mac(root, $"{clientId}:{day:yyyy-MM-dd}");
    }

    public static string Verifier(string root, string random) => Mac(root, $"pkce:{random}");

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string CreateState(string root, string nonce, DateTimeOffset now)
    {
        var random = Base64Url(RandomNumberGenerator.GetBytes(24));
        var expires = now.Add(StateLifetime).ToUnixTimeSeconds();
        return $"{random}.{expires}.{Mac(root, $"state:{random}:{expires}:{nonce}")}";
    }

    /// <returns>The random part, or null when the state is forged, expired or from another browser.</returns>
    public static string? ValidateState(string root, string state, string nonce, DateTimeOffset now)
    {
        var parts = state.Split('.');

        if (parts.Length != 3 || !long.TryParse(parts[1], out var expires) || expires < now.ToUnixTimeSeconds())
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(Mac(root, $"state:{parts[0]}:{parts[1]}:{nonce}"));

        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(parts[2])) ? parts[0] : null;
    }

    private static string Mac(string root, string message)
        => Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(root), Encoding.UTF8.GetBytes(message)));

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record VelosoStartRequest(string Nonce);

public sealed record VelosoStartResponse(string Url);

public sealed record VelosoCompleteRequest(string Code, string State, string Nonce);

public sealed record VelosoOptionsResponse(bool VelosoId, bool PasswordLogin);

/// <summary>Entering through the Portal. Identity, contract and finance were all decided there.</summary>
public sealed class VelosoIdService(
    VelosoIdOptions options,
    IVelosoIdGateway gateway,
    IRepository<User> users,
    ITokenIssuer tokens,
    ISecretProtector protector,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly Error Failed = Error.Unauthorized("veloso_id.failed", "Não foi possível concluir o acesso. Tente novamente.");

    private static readonly Error NotLinked = Error.Forbidden(
        "veloso_id.not_linked",
        "Sua identidade foi confirmada, mas este acesso ainda não foi vinculado ao To-No-Rango. Fale com o Suporte da Veloso Solution.");

    public VelosoOptionsResponse Describe() => new(options.Enabled, options.PasswordLoginEnabled || !options.Enabled);

    public Result<VelosoStartResponse> Start(VelosoStartRequest request)
    {
        if (!options.Enabled)
        {
            return Result<VelosoStartResponse>.Failure(Failed);
        }

        if (string.IsNullOrWhiteSpace(request.Nonce) || request.Nonce.Length is < 16 or > 128)
        {
            return Result<VelosoStartResponse>.Failure(AppErrors.Validation("Requisição inválida."));
        }

        var state = VelosoIdProtocol.CreateState(options.RootSecret, request.Nonce, clock.UtcNow);
        var verifier = VelosoIdProtocol.Verifier(options.RootSecret, state.Split('.')[0]);

        var query = string.Join('&', new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["state"] = state,
            ["code_challenge"] = VelosoIdProtocol.Challenge(verifier),
            ["code_challenge_method"] = "S256",
        }.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        return new VelosoStartResponse($"{options.PortalWebUrl.TrimEnd('/')}/veloso-id?{query}");
    }

    public async Task<Result<LoginResponse>> CompleteAsync(VelosoCompleteRequest request, CancellationToken cancellationToken)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.Nonce))
        {
            return Result<LoginResponse>.Failure(Failed);
        }

        var now = clock.UtcNow;
        var random = VelosoIdProtocol.ValidateState(options.RootSecret, request.State, request.Nonce, now);

        if (random is null)
        {
            return Result<LoginResponse>.Failure(Failed);
        }

        var verifier = VelosoIdProtocol.Verifier(options.RootSecret, random);
        var authorization = await gateway.RedeemAsync(request.Code, options.RedirectUri, verifier, cancellationToken);

        if (authorization is null)
        {
            return Result<LoginResponse>.Failure(Failed);
        }

        var email = User.NormalizeEmail(authorization.Person.Email);
        var user = await users.FirstOrDefaultAsync(item => item.Email == email, cancellationToken);

        if (user is null || !user.IsActive)
        {
            await gateway.EndAsync(authorization.SessionHandle, cancellationToken);
            return Result<LoginResponse>.Failure(NotLinked);
        }

        user.RegisterSuccessfulLogin(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = tokens.Issue(user.Id, user.TenantId, user.Name, user.Email, user.Role.ToString(), protector.Protect(authorization.SessionHandle));

        return new LoginResponse(token, expiresAt, UserDto.From(user));
    }

    public async Task<Result> LogoutAsync(string? protectedHandle, CancellationToken cancellationToken)
    {
        if (options.Enabled && !string.IsNullOrWhiteSpace(protectedHandle))
        {
            await gateway.EndAsync(protector.Unprotect(protectedHandle), cancellationToken);
        }

        return Result.Success();
    }
}
