using System.Security.Cryptography;
using System.Text;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Messaging;
using ToNoRango.Domain.Zest;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Zest;

public sealed record ZestConnectionDto(bool IsConnected, string? KeyHint, DateTimeOffset? CreatedAt, DateTimeOffset? LastUsedAt, int MessagesRelayed, string? Endpoint);

/// <summary>The key is returned only here, once.</summary>
public sealed record ZestConnectionKeyDto(string Key, ZestConnectionDto Connection);

/// <summary>
/// The bridge to ZEST: the AI lives there, the store data and the sale live here. ZEST sends the
/// customer message (text or audio) with the store key and receives the replies to deliver.
/// </summary>
public sealed class ZestConnectorService(
    IRepository<ZestConnection> connections,
    IEstablishmentRepository establishments,
    WhatsAppService whatsApp,
    IModuleAccess modules,
    IAppUrls urls,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<ZestConnectionDto>> GetAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        ToDto(establishmentId, await connections.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken));

    /// <summary>Creates or replaces the key. The old key stops working at once.</summary>
    public Task<Result<ZestConnectionKeyDto>> CreateKeyAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<ZestConnectionKeyDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<ZestConnectionKeyDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var key = "tnr_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            var hint = key[^6..];
            var connection = await connections.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (connection is null)
            {
                connection = new ZestConnection(establishment.TenantId, establishmentId, Hash(key), hint, clock.UtcNow);
                connections.Add(connection);
            }
            else
            {
                connection.Rotate(Hash(key), hint, clock.UtcNow);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new ZestConnectionKeyDto(key, ToDto(establishmentId, connection));
        });

    public async Task<Result<bool>> DisconnectAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var connection = await connections.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        if (connection is not null)
        {
            connections.Remove(connection);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>Checks the key in constant time and relays the message through the same flow as WhatsApp.</summary>
    public async Task<Result<ConnectorReplyDto>> RelayAsync(Guid establishmentId, string? key, ConnectorMessageRequest request, CancellationToken cancellationToken)
    {
        var connection = await connections.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        if (connection is null
            || string.IsNullOrWhiteSpace(key)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(connection.KeyHash), Encoding.ASCII.GetBytes(Hash(key.Trim()))))
        {
            return Result<ConnectorReplyDto>.Failure(Error.Unauthorized("zest.key", "Chave do conector inválida."));
        }

        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null || !establishment.IsActive)
        {
            return Result<ConnectorReplyDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        if (!await modules.IsAllowedAsync(establishment.TenantId, Modules.Ia, cancellationToken))
        {
            return Result<ConnectorReplyDto>.Failure(Error.Forbidden("contract.blocked", "O módulo de IA não está contratado para esta loja."));
        }

        connection.Used(clock.UtcNow);
        return await whatsApp.RelayFromZestAsync(establishment, request, cancellationToken);
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private ZestConnectionDto ToDto(Guid establishmentId, ZestConnection? connection) => new(
        connection is not null,
        connection?.KeyHint,
        connection?.CreatedAt,
        connection?.LastUsedAt,
        connection?.MessagesRelayed ?? 0,
        urls.PublicApiUrl is null ? null : $"{urls.PublicApiUrl}/api/zest/{establishmentId}/messages");
}
