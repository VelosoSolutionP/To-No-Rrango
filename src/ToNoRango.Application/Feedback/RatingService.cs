using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Feedback;
using ToNoRango.Domain.Orders;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Feedback;

public sealed record RateOrderRequest(
    int ServiceScore,
    int ProductScore,
    int OverallScore,
    string? StoreComment,
    int? CourierServiceScore,
    int? CourierPunctualityScore,
    int? CourierCareScore,
    string? CourierComment);

/// <summary>What the tracking page needs: whether the order can be rated, was rated, and who delivered it.</summary>
public sealed record RatingStateDto(bool CanRate, bool IsRated, string? CourierName, OrderRatingDto? Rating);

public sealed record OrderRatingDto(
    string OrderCode,
    int ServiceScore,
    int ProductScore,
    int OverallScore,
    string? StoreComment,
    string? CourierName,
    int? CourierServiceScore,
    int? CourierPunctualityScore,
    int? CourierCareScore,
    string? CourierComment,
    DateTimeOffset CreatedAt)
{
    public static OrderRatingDto From(OrderRating rating) => new(
        rating.OrderCode,
        rating.ServiceScore,
        rating.ProductScore,
        rating.OverallScore,
        rating.StoreComment,
        rating.CourierName,
        rating.CourierServiceScore,
        rating.CourierPunctualityScore,
        rating.CourierCareScore,
        rating.CourierComment,
        rating.CreatedAt);
}

public sealed record ScoreAverages(double? Service, double? Product, double? Overall);

public sealed record CourierScoreDto(string CourierName, int Count, double? Service, double? Punctuality, double? Care);

/// <summary>A dimension that keeps getting low scores (1 or 2) in the period.</summary>
public sealed record RecurringProblemDto(string Dimension, int LowScores, int Total);

public sealed record RatingSummaryDto(
    int Days,
    int Count,
    ScoreAverages Store,
    IReadOnlyList<CourierScoreDto> Couriers,
    IReadOnlyList<RecurringProblemDto> Problems,
    IReadOnlyList<OrderRatingDto> Recent);

public sealed class RatingService(
    IOrderRepository orders,
    IRepository<OrderRating> ratings,
    IOrderEvents events,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<RatingStateDto>> GetStateAsync(string code, CancellationToken cancellationToken)
    {
        var order = await orders.GetByCodeAsync(code, cancellationToken);

        if (order is null)
        {
            return Result<RatingStateDto>.Failure(AppErrors.NotFound("order.not_found", "Pedido não encontrado."));
        }

        var rating = await ratings.FirstOrDefaultAsync(candidate => candidate.OrderId == order.Id, cancellationToken);

        return new RatingStateDto(
            order.Status == OrderStatus.Delivered && rating is null,
            rating is not null,
            order.CourierName,
            rating is null ? null : OrderRatingDto.From(rating));
    }

    public Task<Result<RatingStateDto>> RateAsync(string code, RateOrderRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<RatingStateDto>(async () =>
        {
            var order = await orders.GetByCodeAsync(code, cancellationToken);

            if (order is null)
            {
                return Result<RatingStateDto>.Failure(AppErrors.NotFound("order.not_found", "Pedido não encontrado."));
            }

            if (order.Status != OrderStatus.Delivered)
            {
                return Result<RatingStateDto>.Failure(AppErrors.Validation("A avaliação abre quando o pedido é entregue."));
            }

            if (await ratings.AnyAsync(candidate => candidate.OrderId == order.Id, cancellationToken))
            {
                return Result<RatingStateDto>.Failure(AppErrors.Conflict("rating.duplicate", "Este pedido já foi avaliado. Obrigado!"));
            }

            var rating = new OrderRating(
                order.TenantId,
                order.EstablishmentId,
                order.Id,
                order.Code,
                request.ServiceScore,
                request.ProductScore,
                request.OverallScore,
                request.StoreComment,
                order.CourierId,
                order.CourierName,
                request.CourierServiceScore,
                request.CourierPunctualityScore,
                request.CourierCareScore,
                request.CourierComment,
                clock.UtcNow);

            ratings.Add(rating);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(order.Code);

            return new RatingStateDto(false, true, order.CourierName, OrderRatingDto.From(rating));
        });

    public async Task<Result<RatingSummaryDto>> SummaryAsync(Guid establishmentId, int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 366);
        var from = clock.UtcNow.AddDays(-days);
        var list = await ratings.ListAsync(
            rating => rating.EstablishmentId == establishmentId && rating.CreatedAt >= from,
            rating => rating.CreatedAt,
            cancellationToken,
            descending: true,
            take: 5000);

        double? Average(IEnumerable<int?> values)
        {
            var present = values.Where(value => value is not null).Select(value => value!.Value).ToList();
            return present.Count == 0 ? null : Math.Round(present.Average(), 2);
        }

        var couriers = list
            .Where(rating => rating.CourierName is not null)
            .GroupBy(rating => rating.CourierName!)
            .Select(group => new CourierScoreDto(
                group.Key,
                group.Count(),
                Average(group.Select(rating => rating.CourierServiceScore)),
                Average(group.Select(rating => rating.CourierPunctualityScore)),
                Average(group.Select(rating => rating.CourierCareScore))))
            .OrderBy(courier => courier.CourierName)
            .ToList();

        var dimensions = new (string Name, Func<OrderRating, int?> Score)[]
        {
            ("Atendimento", rating => rating.ServiceScore),
            ("Produtos", rating => rating.ProductScore),
            ("Experiência geral", rating => rating.OverallScore),
            ("Atendimento do entregador", rating => rating.CourierServiceScore),
            ("Pontualidade da entrega", rating => rating.CourierPunctualityScore),
            ("Cuidado com o pedido", rating => rating.CourierCareScore),
        };

        // Recurring means at least two low scores and at least a fifth of the answers.
        var problems = dimensions
            .Select(dimension =>
            {
                var scores = list.Select(dimension.Score).Where(score => score is not null).ToList();
                return new RecurringProblemDto(dimension.Name, scores.Count(score => score <= 2), scores.Count);
            })
            .Where(problem => problem.LowScores >= 2 && problem.LowScores * 5 >= problem.Total)
            .OrderByDescending(problem => problem.LowScores)
            .ToList();

        return new RatingSummaryDto(
            days,
            list.Count,
            new ScoreAverages(
                Average(list.Select(rating => (int?)rating.ServiceScore)),
                Average(list.Select(rating => (int?)rating.ProductScore)),
                Average(list.Select(rating => (int?)rating.OverallScore))),
            couriers,
            problems,
            list.Take(50).Select(OrderRatingDto.From).ToList());
    }
}
