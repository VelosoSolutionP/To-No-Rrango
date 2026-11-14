using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Application.Contracts;
using ToNoRango.Application.Orders;
using ToNoRango.Domain.Orders;
using ToNoRango.Domain.Payments;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Payments;

public sealed record PaymentSettingsRequest(
    string? AccessToken,
    string? WebhookSecret,
    string? PublicKey,
    bool IsPixEnabled,
    bool IsCardEnabled);

public sealed record PaymentSettingsDto(
    bool HasAccessToken,
    bool HasWebhookSecret,
    string? PublicKey,
    bool IsPixEnabled,
    bool IsCardEnabled,
    string? WebhookUrl,
    bool UsesPlatformAccount = false);

/// <summary>
/// The Veloso Solution Mercado Pago account, the same one the ZEST charges with. Every store that has not
/// linked its own account charges Pix and card through it.
/// </summary>
public sealed record PlatformPaymentAccount(string? AccessToken, string? PublicKey, string? WebhookSecret)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccessToken);
}

public sealed record CheckoutOptionsDto(bool PixOnline, bool CardOnline, string? PublicKey, bool HasDeliveryZones, bool CheckoutLink = false);

public sealed record CheckoutLinkDto(string Url);

public sealed record ConfirmCheckoutRequest(string PaymentId);

public sealed record CreatePixRequest(string? PayerEmail);

public sealed record CreateCardRequest(
    string Token,
    string PaymentMethodId,
    string? IssuerId,
    int Installments,
    string PayerEmail,
    string? DocumentType,
    string? DocumentNumber);

public sealed record PaymentDto(
    PaymentChannel Channel,
    ChargeStatus Status,
    string? StatusDetail,
    long AmountInCents,
    string? PixQrCode,
    string? PixQrCodeBase64,
    DateTimeOffset? ExpiresAt)
{
    public static PaymentDto From(Payment payment) => new(
        payment.Channel,
        payment.Status,
        payment.StatusDetail,
        payment.AmountInCents,
        payment.PixQrCode,
        payment.PixQrCodeBase64,
        payment.ExpiresAt);
}

public sealed class PaymentService(
    IRepository<Payment> payments,
    IRepository<PaymentSettings> settingsRepository,
    IOrderRepository orders,
    IEstablishmentRepository establishments,
    IPaymentGateway gateway,
    ISecretProtector protector,
    OrderLifecycle lifecycle,
    IModuleAccess modules,
    IAppUrls urls,
    IOrderEvents events,
    IUnitOfWork unitOfWork,
    IClock clock,
    PlatformPaymentAccount platform)
{
    private static readonly TimeSpan GatewayPollInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckoutLifetime = TimeSpan.FromHours(2);
    private const string CheckoutReferencePrefix = "checkout:";
    private static readonly Error OrderNotFound = Error.NotFound("order.not_found", "Pedido não encontrado.");
    private static readonly Error GatewayUnavailable =
        Error.External("payment.gateway", "Não foi possível falar com o Mercado Pago. Tente novamente em instantes.");

    public async Task<Result<PaymentSettingsDto>> GetSettingsAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<PaymentSettingsDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        return ToDto(establishmentId, settings) with { UsesPlatformAccount = settings?.EncryptedAccessToken is null && platform.IsConfigured };
    }

    public Task<Result<PaymentSettingsDto>> SaveSettingsAsync(Guid establishmentId, PaymentSettingsRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<PaymentSettingsDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<PaymentSettingsDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var settings = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

            if (settings is null)
            {
                settings = new PaymentSettings(establishment.TenantId, establishment.Id, clock.UtcNow);
                settingsRepository.Add(settings);
            }

            settings.Update(
                string.IsNullOrWhiteSpace(request.AccessToken) ? null : protector.Protect(request.AccessToken.Trim()),
                string.IsNullOrWhiteSpace(request.WebhookSecret) ? null : protector.Protect(request.WebhookSecret.Trim()),
                request.PublicKey,
                request.IsPixEnabled,
                request.IsCardEnabled,
                clock.UtcNow);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(establishmentId, settings);
        });

    public async Task<CheckoutOptionsDto> GetCheckoutOptionsAsync(Guid tenantId, Guid establishmentId, bool hasZones, CancellationToken cancellationToken)
    {
        var settings = await SettingsOfAsync(establishmentId, cancellationToken);
        var allowed = await modules.IsAllowedAsync(tenantId, Modules.Pagamentos, cancellationToken);

        return new CheckoutOptionsDto(
            allowed && settings is { CanChargePix: true },
            allowed && settings is { CanChargeCard: true },
            allowed && settings is { CanChargeCard: true } ? settings.PublicKey : null,
            hasZones,
            allowed && settings is { CanChargeCheckout: true });
    }

    public Task<Result<PaymentDto>> CreatePixAsync(string orderCode, CreatePixRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<PaymentDto>(async () =>
        {
            var (order, settings, error) = await PrepareAsync(orderCode, cancellationToken);

            if (error is not null)
            {
                return Result<PaymentDto>.Failure(error);
            }

            if (!settings!.CanChargePix)
            {
                return Result<PaymentDto>.Failure(AppErrors.Validation("Pix online não está habilitado neste estabelecimento."));
            }

            var existing = await payments.ListAsync(
                payment => payment.OrderId == order!.Id,
                payment => payment.CreatedAt,
                cancellationToken,
                descending: true,
                take: 50);

            var reusable = existing.FirstOrDefault(payment => payment.Channel == PaymentChannel.Pix && payment.IsReusable(clock.UtcNow));

            if (reusable is not null)
            {
                return PaymentDto.From(reusable);
            }

            GatewayPayment created;

            try
            {
                created = await gateway.CreatePixAsync(
                    protector.Unprotect(settings.EncryptedAccessToken!),
                    new PixCharge(
                        order!.TotalInCents,
                        $"Pedido {order.Code}",
                        order.Code,
                        PayerEmailOf(request.PayerEmail, order),
                        $"pix-{order.Id:N}-{existing.Count}",
                        WebhookUrlOf(order.EstablishmentId)),
                    cancellationToken);
            }
            catch (PaymentGatewayException)
            {
                return Result<PaymentDto>.Failure(GatewayUnavailable);
            }

            var payment = new Payment(
                order.TenantId,
                order.EstablishmentId,
                order.Id,
                created.Id,
                PaymentChannel.Pix,
                MapStatus(created.Status),
                created.StatusDetail,
                order.TotalInCents,
                created.PixQrCode,
                created.PixQrCodeBase64,
                created.ExpiresAt,
                clock.UtcNow);

            payments.Add(payment);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return PaymentDto.From(payment);
        });

    /// <summary>The payment only exists once the customer pays on the link, so it is recorded when Mercado Pago reports it.</summary>
    public Task<Result<CheckoutLinkDto>> CreateCheckoutLinkAsync(string orderCode, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CheckoutLinkDto>(async () =>
        {
            var (order, settings, error) = await PrepareAsync(orderCode, cancellationToken);

            if (error is not null)
            {
                return Result<CheckoutLinkDto>.Failure(error);
            }

            if (!settings!.CanChargeCheckout)
            {
                return Result<CheckoutLinkDto>.Failure(AppErrors.Validation("Pagamento online indisponível neste estabelecimento."));
            }

            try
            {
                var checkout = await gateway.CreateCheckoutAsync(
                    protector.Unprotect(settings.EncryptedAccessToken!),
                    new CheckoutCharge(
                        order!.TotalInCents,
                        $"Pedido {order.Code}",
                        CheckoutReference(order.Code),
                        urls.TrackingUrl(order.Code),
                        clock.UtcNow.Add(CheckoutLifetime),
                        WebhookUrlOf(order.EstablishmentId)),
                    cancellationToken);

                return new CheckoutLinkDto(checkout.Url);
            }
            catch (PaymentGatewayException)
            {
                return Result<CheckoutLinkDto>.Failure(GatewayUnavailable);
            }
        });

    /// <summary>The customer came back from the link. Covers a notification that was lost or has not arrived yet.</summary>
    public Task<Result<PaymentDto?>> ConfirmCheckoutAsync(string orderCode, ConfirmCheckoutRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<PaymentDto?>(async () =>
        {
            var order = await orders.GetByCodeAsync(orderCode, cancellationToken);

            if (order is null)
            {
                return Result<PaymentDto?>.Failure(OrderNotFound);
            }

            var settings = await SettingsOfAsync(order.EstablishmentId, cancellationToken);

            if (settings?.EncryptedAccessToken is null || string.IsNullOrWhiteSpace(request.PaymentId))
            {
                return Result<PaymentDto?>.Success(null);
            }

            GatewayPayment remote;

            try
            {
                remote = await gateway.GetAsync(protector.Unprotect(settings.EncryptedAccessToken), request.PaymentId, cancellationToken);
            }
            catch (PaymentGatewayException)
            {
                return Result<PaymentDto?>.Failure(GatewayUnavailable);
            }

            if (OrderCodeOfCheckout(remote.ExternalReference) != order.Code)
            {
                return Result<PaymentDto?>.Failure(AppErrors.Validation("Este pagamento não pertence a este pedido."));
            }

            var payment = await SettleCheckoutAsync(order, remote, cancellationToken);
            await events.PublishAsync(order.Code);

            return PaymentDto.From(payment);
        });

    public Task<Result<PaymentDto>> CreateCardAsync(string orderCode, CreateCardRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<PaymentDto>(async () =>
        {
            var (order, settings, error) = await PrepareAsync(orderCode, cancellationToken);

            if (error is not null)
            {
                return Result<PaymentDto>.Failure(error);
            }

            if (!settings!.CanChargeCard)
            {
                return Result<PaymentDto>.Failure(AppErrors.Validation("Cartão online não está habilitado neste estabelecimento."));
            }

            if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.PaymentMethodId) || request.Installments < 1)
            {
                return Result<PaymentDto>.Failure(AppErrors.Validation("Dados do cartão incompletos."));
            }

            var attempts = await payments.CountAsync(payment => payment.OrderId == order!.Id, cancellationToken);
            GatewayPayment created;

            try
            {
                created = await gateway.CreateCardAsync(
                    protector.Unprotect(settings.EncryptedAccessToken!),
                    new CardCharge(
                        order!.TotalInCents,
                        $"Pedido {order.Code}",
                        order.Code,
                        request.Token,
                        request.PaymentMethodId,
                        request.IssuerId,
                        request.Installments,
                        PayerEmailOf(request.PayerEmail, order),
                        request.DocumentType,
                        request.DocumentNumber,
                        $"card-{order.Id:N}-{attempts}",
                        WebhookUrlOf(order.EstablishmentId)),
                    cancellationToken);
            }
            catch (PaymentGatewayException)
            {
                return Result<PaymentDto>.Failure(GatewayUnavailable);
            }

            var dto = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var payment = new Payment(
                    order.TenantId,
                    order.EstablishmentId,
                    order.Id,
                    created.Id,
                    PaymentChannel.Card,
                    ChargeStatus.Pending,
                    created.StatusDetail,
                    order.TotalInCents,
                    null,
                    null,
                    null,
                    clock.UtcNow);

                payments.Add(payment);
                await ApplyGatewayStateAsync(payment, order, created, token);

                return PaymentDto.From(payment);
            }, cancellationToken);

            await events.PublishAsync(order.Code);

            return dto;
        });

    /// <summary>Latest charge of the order. While pending it re-checks the gateway, covering lost webhooks.</summary>
    public Task<Result<PaymentDto?>> GetCurrentAsync(string orderCode, CancellationToken cancellationToken) =>
        Guarded.RunAsync<PaymentDto?>(async () =>
        {
            var order = await orders.GetByCodeAsync(orderCode, cancellationToken);

            if (order is null)
            {
                return Result<PaymentDto?>.Failure(OrderNotFound);
            }

            var latest = await payments.ListAsync(
                payment => payment.OrderId == order.Id,
                payment => payment.CreatedAt,
                cancellationToken,
                descending: true,
                take: 1);

            if (latest.Count == 0)
            {
                return Result<PaymentDto?>.Success(null);
            }

            if (latest[0].Status != ChargeStatus.Pending || clock.UtcNow - latest[0].LastCheckedAt < GatewayPollInterval)
            {
                return PaymentDto.From(latest[0]);
            }

            var settings = await SettingsOfAsync(order.EstablishmentId, cancellationToken);

            if (settings?.EncryptedAccessToken is null)
            {
                return PaymentDto.From(latest[0]);
            }

            var payment = await payments.FindAsync(latest[0].Id, cancellationToken);
            GatewayPayment remote;

            try
            {
                remote = await gateway.GetAsync(protector.Unprotect(settings.EncryptedAccessToken), payment!.ProviderPaymentId, cancellationToken);
            }
            catch (PaymentGatewayException)
            {
                return PaymentDto.From(latest[0]);
            }

            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await ApplyGatewayStateAsync(payment, order, remote, token);
                return true;
            }, cancellationToken);

            await events.PublishAsync(order.Code);

            return PaymentDto.From(payment);
        });

    /// <summary>
    /// The notification only says something changed: the state applied always comes from a fresh
    /// read of the gateway with the establishment's own token, so a forged call cannot mark an order paid.
    /// </summary>
    public Task<Result<bool>> HandleWebhookAsync(
        Guid establishmentId,
        string? dataId,
        string? signature,
        string? requestId,
        CancellationToken cancellationToken) =>
        Guarded.RunAsync<bool>(async () =>
        {
            var settings = await SettingsOfAsync(establishmentId, cancellationToken);

            if (settings?.EncryptedAccessToken is null || string.IsNullOrWhiteSpace(dataId))
            {
                return true;
            }

            if (settings.EncryptedWebhookSecret is not null
                && !MercadoPagoSignature.IsValid(protector.Unprotect(settings.EncryptedWebhookSecret), signature, requestId, dataId))
            {
                return Result<bool>.Failure(Error.Unauthorized("webhook.signature", "Assinatura inválida."));
            }

            GatewayPayment remote;

            try
            {
                remote = await gateway.GetAsync(protector.Unprotect(settings.EncryptedAccessToken), dataId, cancellationToken);
            }
            catch (PaymentGatewayException)
            {
                return Result<bool>.Failure(GatewayUnavailable);
            }

            var payment = await payments.FirstOrDefaultAsync(
                item => item.ProviderPaymentId == remote.Id && item.EstablishmentId == establishmentId,
                cancellationToken);

            if (payment is null && OrderCodeOfCheckout(remote.ExternalReference) is { } checkoutCode)
            {
                var checkoutOrder = await orders.GetByCodeAsync(checkoutCode, cancellationToken);

                if (checkoutOrder is null || checkoutOrder.EstablishmentId != establishmentId)
                {
                    return Result<bool>.Failure(OrderNotFound);
                }

                await SettleCheckoutAsync(checkoutOrder, remote, cancellationToken);
                await events.PublishAsync(checkoutOrder.Code);

                return true;
            }

            if (payment is null)
            {
                // A notification can arrive before the charge is saved; a 404 makes Mercado Pago retry.
                return Result<bool>.Failure(AppErrors.NotFound("payment.not_found", "Pagamento não encontrado."));
            }

            var order = await orders.GetByIdAsync(payment.OrderId, cancellationToken);

            if (order is null)
            {
                return Result<bool>.Failure(OrderNotFound);
            }

            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await ApplyGatewayStateAsync(payment, order, remote, token);
                return true;
            }, cancellationToken);

            await events.PublishAsync(order.Code);

            return true;
        });

    public Task<Result<OrderDto>> RefundAsync(Guid orderId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<OrderDto>(async () =>
        {
            var order = await orders.GetByIdAsync(orderId, cancellationToken);

            if (order is null)
            {
                return Result<OrderDto>.Failure(OrderNotFound);
            }

            if (order.PaymentStatus != PaymentStatus.Paid)
            {
                return Result<OrderDto>.Failure(AppErrors.Validation("Somente pedidos pagos podem ser estornados."));
            }

            var payment = await payments.FirstOrDefaultAsync(
                item => item.OrderId == order.Id && item.Status == ChargeStatus.Approved,
                cancellationToken);

            if (payment is not null)
            {
                var settings = await SettingsOfAsync(order.EstablishmentId, cancellationToken);

                if (settings?.EncryptedAccessToken is null)
                {
                    return Result<OrderDto>.Failure(AppErrors.Validation("Configure o Mercado Pago para estornar este pedido."));
                }

                try
                {
                    await gateway.RefundAsync(
                        protector.Unprotect(settings.EncryptedAccessToken),
                        payment.ProviderPaymentId,
                        $"refund-{payment.Id:N}",
                        cancellationToken);
                }
                catch (PaymentGatewayException)
                {
                    return Result<OrderDto>.Failure(GatewayUnavailable);
                }
            }

            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                payment?.UpdateStatus(ChargeStatus.Refunded, "refunded_by_staff", clock.UtcNow);
                await lifecycle.MarkRefundedAsync(order, token);
                return true;
            }, cancellationToken);

            await events.PublishAsync(order.Code);

            return OrderDto.From(order);
        });

    public static ChargeStatus MapStatus(string status) => status switch
    {
        "approved" => ChargeStatus.Approved,
        "rejected" => ChargeStatus.Rejected,
        "cancelled" => ChargeStatus.Cancelled,
        "refunded" or "charged_back" => ChargeStatus.Refunded,
        _ => ChargeStatus.Pending
    };

    public static string CheckoutReference(string orderCode) => $"{CheckoutReferencePrefix}{orderCode}";

    /// <summary>Only links carry this prefix: a Pix or card reported before its own row is saved must keep waiting for it.</summary>
    public static string? OrderCodeOfCheckout(string? externalReference) =>
        externalReference is not null && externalReference.StartsWith(CheckoutReferencePrefix, StringComparison.Ordinal)
            ? externalReference[CheckoutReferencePrefix.Length..]
            : null;

    private Task<Payment> SettleCheckoutAsync(Order order, GatewayPayment remote, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var payment = await payments.FirstOrDefaultAsync(
                item => item.ProviderPaymentId == remote.Id && item.EstablishmentId == order.EstablishmentId,
                token);

            if (payment is null)
            {
                payment = new Payment(
                    order.TenantId,
                    order.EstablishmentId,
                    order.Id,
                    remote.Id,
                    PaymentChannel.Checkout,
                    ChargeStatus.Pending,
                    remote.StatusDetail,
                    order.TotalInCents,
                    null,
                    null,
                    null,
                    clock.UtcNow);

                payments.Add(payment);
            }

            await ApplyGatewayStateAsync(payment, order, remote, token);

            return payment;
        }, cancellationToken);

    private async Task ApplyGatewayStateAsync(Payment payment, Order order, GatewayPayment remote, CancellationToken cancellationToken)
    {
        var status = MapStatus(remote.Status);

        if (status == ChargeStatus.Approved && remote.AmountInCents != payment.AmountInCents)
        {
            payment.UpdateStatus(ChargeStatus.Pending, $"amount_mismatch:{remote.AmountInCents}", clock.UtcNow);
            return;
        }

        var changed = payment.Status != status;
        payment.UpdateStatus(status, remote.StatusDetail, clock.UtcNow);

        if (status == ChargeStatus.Approved)
        {
            await lifecycle.MarkPaidAsync(order, cancellationToken);

            if (changed && order.Status == OrderStatus.Pending)
            {
                await lifecycle.ChangeStatusAsync(order, OrderStatus.Confirmed, "mercadopago", "Pagamento aprovado.", cancellationToken);
            }
        }
        else if (status == ChargeStatus.Refunded && order.PaymentStatus == PaymentStatus.Paid)
        {
            await lifecycle.MarkRefundedAsync(order, cancellationToken);
        }
    }

    private async Task<(Order? Order, PaymentSettings? Settings, Error? Error)> PrepareAsync(string orderCode, CancellationToken cancellationToken)
    {
        var order = await orders.GetByCodeAsync(orderCode, cancellationToken);

        if (order is null)
        {
            return (null, null, OrderNotFound);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return (null, null, AppErrors.Validation("Este pedido foi cancelado."));
        }

        if (order.PaymentStatus != PaymentStatus.Pending)
        {
            return (null, null, AppErrors.Conflict("payment.already_processed", "O pagamento deste pedido já foi processado."));
        }

        if (!await modules.IsAllowedAsync(order.TenantId, Modules.Pagamentos, cancellationToken))
        {
            return (null, null, AppErrors.Validation("Pagamento online indisponível neste estabelecimento."));
        }

        var settings = await SettingsOfAsync(order.EstablishmentId, cancellationToken);

        return settings is null
            ? (null, null, AppErrors.Validation("Pagamento online indisponível neste estabelecimento."))
            : (order, settings, null);
    }

    /// <summary>The store's own Mercado Pago account; without one, the Veloso account (never saved, built per call).</summary>
    private async Task<PaymentSettings?> SettingsOfAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var own = await settingsRepository.FirstOrDefaultAsync(item => item.EstablishmentId == establishmentId, cancellationToken);

        if (own?.EncryptedAccessToken is not null || !platform.IsConfigured)
        {
            return own;
        }

        var tenantId = own?.TenantId ?? (await establishments.GetByIdAsync(establishmentId, cancellationToken))?.TenantId;

        if (tenantId is null)
        {
            return own;
        }

        var shared = new PaymentSettings(tenantId.Value, establishmentId, clock.UtcNow);
        shared.Update(
            protector.Protect(platform.AccessToken!.Trim()),
            string.IsNullOrWhiteSpace(platform.WebhookSecret) ? null : protector.Protect(platform.WebhookSecret.Trim()),
            platform.PublicKey,
            isPixEnabled: true,
            isCardEnabled: true,
            clock.UtcNow);
        return shared;
    }

    private PaymentSettingsDto ToDto(Guid establishmentId, PaymentSettings? settings) => new(
        settings?.EncryptedAccessToken is not null,
        settings?.EncryptedWebhookSecret is not null,
        settings?.PublicKey,
        settings?.IsPixEnabled ?? false,
        settings?.IsCardEnabled ?? false,
        WebhookUrlOf(establishmentId));

    private string? WebhookUrlOf(Guid establishmentId) =>
        urls.PublicApiUrl is null ? null : $"{urls.PublicApiUrl}/api/webhooks/mercadopago/{establishmentId}";

    private static string PayerEmailOf(string? email, Order order) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@')
            ? email.Trim()
            : $"cliente.{order.Code.ToLowerInvariant()}@tonorango.velososolution.com.br";
}
