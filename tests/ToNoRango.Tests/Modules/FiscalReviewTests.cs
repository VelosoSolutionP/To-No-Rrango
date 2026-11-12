using System.IO.Compression;
using System.Linq.Expressions;
using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Fiscal;
using ToNoRango.Domain.Catalog;
using ToNoRango.Domain.Fiscal;
using ToNoRango.Domain.Inventory;

namespace ToNoRango.Tests.Modules;

internal sealed class MemoryRepository<T>(params T[] items) : IRepository<T>
    where T : class
{
    private readonly List<T> rows = [.. items];

    public Task<T?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) =>
        Task.FromResult(rows.AsQueryable().FirstOrDefault(predicate));

    public Task<IReadOnlyList<T>> ListAsync<TKey>(Expression<Func<T, bool>> predicate, Expression<Func<T, TKey>> orderBy, CancellationToken cancellationToken, bool descending = false, int take = 500)
    {
        var query = rows.AsQueryable().Where(predicate);
        query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
        return Task.FromResult<IReadOnlyList<T>>(query.Take(take).ToList());
    }

    public Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) => Task.FromResult(rows.AsQueryable().Count(predicate));

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) => Task.FromResult(rows.AsQueryable().Any(predicate));

    public void Add(T entity) => rows.Add(entity);

    public void Remove(T entity) => rows.Remove(entity);
}

public class FiscalReviewTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();

    private static FiscalProfile Profile()
    {
        var profile = new FiscalProfile(Tenant, Store);
        profile.Update("LOJA TESTE LTDA", "Loja", "11222333000181", "0623079040081", "Rua A", "1", "Centro", "Contagem", "MG", "32000000", "3118601", 1, 1);
        return profile;
    }

    [Fact]
    public async Task Conferencia_ApontaRejeicaoComExplicacaoECompraNaoLancada()
    {
        var rejected = new FiscalDocument(Tenant, Store, Guid.NewGuid(), "P1", 1, 7, 2, 1000, DateTimeOffset.UtcNow);
        rejected.Reject("778 - Rejeicao: Informado NCM inexistente", DateTimeOffset.UtcNow);
        var incoming = new ReceivedNote(Tenant, Store, 5, "35261111444777000161550010000012341000012345", "11444777000161", "FORNECEDOR LTDA", DateTimeOffset.UtcNow, 50000, 1, "<nfeProc/>", DateTimeOffset.UtcNow);

        var service = new FiscalReviewService(
            new MemoryRepository<FiscalProfile>(Profile()),
            new MemoryRepository<MenuItem>(),
            new MemoryRepository<ProductFiscalData>(),
            new MemoryRepository<FiscalDocument>(rejected),
            new MemoryRepository<ReceivedNote>(incoming),
            new MemoryRepository<PurchaseEntry>(),
            TimeProvider.System);

        var review = (await service.ReviewAsync(Store, CancellationToken.None)).Value;

        Assert.Contains(review.Findings, item => item.Area == "Rejeições" && item.Action.Contains("NCM"));
        Assert.Contains(review.Findings, item => item.Area == "Compras" && item.Title.Contains("não lançada"));
        Assert.Contains(review.Findings, item => item.Area == "Certificado");
        Assert.Equal(review.Findings.OrderByDescending(item => item.Level), review.Findings);
    }
}
