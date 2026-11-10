using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Feedback;

/// <summary>
/// The customer's review of a delivered order: the store on one side, the courier on the other.
/// Scores go from 1 to 5. Courier scores only exist when the order was delivered by a courier.
/// </summary>
public sealed class OrderRating : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public Guid OrderId { get; private set; }
    public string OrderCode { get; private set; } = string.Empty;
    public int ServiceScore { get; private set; }
    public int ProductScore { get; private set; }
    public int OverallScore { get; private set; }
    public string? StoreComment { get; private set; }
    public Guid? CourierId { get; private set; }
    public string? CourierName { get; private set; }
    public int? CourierServiceScore { get; private set; }
    public int? CourierPunctualityScore { get; private set; }
    public int? CourierCareScore { get; private set; }
    public string? CourierComment { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private OrderRating() { }

    public OrderRating(
        Guid tenantId,
        Guid establishmentId,
        Guid orderId,
        string orderCode,
        int serviceScore,
        int productScore,
        int overallScore,
        string? storeComment,
        Guid? courierId,
        string? courierName,
        int? courierServiceScore,
        int? courierPunctualityScore,
        int? courierCareScore,
        string? courierComment,
        DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        OrderId = Ensure.NotEmpty(orderId, nameof(OrderId));
        OrderCode = Ensure.NotNullOrWhiteSpace(orderCode, nameof(OrderCode));
        ServiceScore = Score(serviceScore, "atendimento");
        ProductScore = Score(productScore, "produtos");
        OverallScore = Score(overallScore, "experiência geral");
        StoreComment = Comment(storeComment);
        CreatedAt = now;

        if (courierId is null)
        {
            return;
        }

        CourierId = courierId;
        CourierName = courierName;
        CourierServiceScore = courierServiceScore is null ? null : Score(courierServiceScore.Value, "atendimento do entregador");
        CourierPunctualityScore = courierPunctualityScore is null ? null : Score(courierPunctualityScore.Value, "pontualidade");
        CourierCareScore = courierCareScore is null ? null : Score(courierCareScore.Value, "cuidado com o pedido");
        CourierComment = Comment(courierComment);
    }

    public double StoreAverage => (ServiceScore + ProductScore + OverallScore) / 3.0;

    public double? CourierAverage
    {
        get
        {
            var scores = new[] { CourierServiceScore, CourierPunctualityScore, CourierCareScore }.Where(score => score is not null).ToList();
            return scores.Count == 0 ? null : scores.Average(score => score!.Value);
        }
    }

    private static int Score(int value, string what) =>
        value is >= 1 and <= 5 ? value : throw new ArgumentOutOfRangeException(what, $"A nota de {what} vai de 1 a 5.");

    private static string? Comment(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Ensure.MaxLength(value.Trim(), 500, "comentário");
}
