using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToNoRango.Application.Abstractions;
using ToNoRango.Domain.Messaging;
using ToNoRango.Infrastructure.Persistence;
using ToNoRango.SharedKernel.Abstractions;

namespace ToNoRango.Infrastructure.Messaging;

/// <summary>Delivers queued WhatsApp messages; SKIP LOCKED lets several API instances share the queue.</summary>
public sealed class OutboxWorker(
    IServiceScopeFactory scopes,
    IWhatsAppSender sender,
    IClock clock,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!sender.IsConfigured)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The WhatsApp outbox batch failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ToNoRangoDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var now = clock.UtcNow;
        var stale = now - StaleClaim;

        var batch = await dbContext.Set<OutboundMessage>()
            .FromSqlInterpolated($"""
                UPDATE messaging.outbound_messages
                SET "Status" = 2, "ClaimedAt" = {now}, "Attempts" = "Attempts" + 1
                WHERE "Id" IN (
                    SELECT "Id" FROM messaging.outbound_messages
                    WHERE ("Status" = 1 AND "NextAttemptAt" <= {now}) OR ("Status" = 2 AND "ClaimedAt" < {stale})
                    ORDER BY "CreatedAt"
                    LIMIT 20
                    FOR UPDATE SKIP LOCKED)
                RETURNING *
                """)
            .IgnoreQueryFilters()
            .AsTracking()
            .ToListAsync(cancellationToken);

        // RETURNING has no guaranteed order, and a customer must read the messages in sequence.
        foreach (var message in batch.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id))
        {
            var settings = await dbContext.Set<WhatsAppSettings>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.EstablishmentId == message.EstablishmentId, cancellationToken);

            try
            {
                if (settings is not { IsReady: true })
                {
                    throw new InvalidOperationException("WhatsApp is not configured for this establishment.");
                }

                var apiKey = protector.Unprotect(settings.EncryptedApiKey!);

                if (message.ImageBase64 is null)
                {
                    await sender.SendTextAsync(settings.InstanceName!, apiKey, message.Phone, message.Text, cancellationToken);
                }
                else
                {
                    await sender.SendImageAsync(settings.InstanceName!, apiKey, message.Phone, message.ImageBase64, message.Text, cancellationToken);
                }

                message.MarkSent(clock.UtcNow);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                message.MarkFailed(exception.Message, clock.UtcNow);
                logger.LogWarning(exception, "Could not send WhatsApp message {MessageId}", message.Id);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
