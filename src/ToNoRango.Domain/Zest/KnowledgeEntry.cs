using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Zest;

/// <summary>Something an establishment taught its own ZEST. Never shared between establishments.</summary>
public sealed class KnowledgeEntry : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string Answer { get; private set; } = string.Empty;
    public string Keywords { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset UpdatedAt { get; private set; }

    private KnowledgeEntry() { }

    public KnowledgeEntry(Guid tenantId, Guid establishmentId, string topic, string answer, string keywords, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Update(topic, answer, keywords, true, now);
    }

    public void Update(string topic, string answer, string keywords, bool isActive, DateTimeOffset now)
    {
        Topic = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(topic, nameof(Topic)), 120, nameof(Topic));
        Answer = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(answer, nameof(Answer)), 1500, nameof(Answer));
        Keywords = Ensure.MaxLength(keywords?.Trim() ?? string.Empty, 300, nameof(Keywords));
        IsActive = isActive;
        UpdatedAt = now;
    }
}

/// <summary>
/// A question the store's bot could not answer. Repeated questions add up, so the store sees
/// what customers keep asking and writes the answer once.
/// </summary>
public sealed class KnowledgeGap : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string Question { get; private set; } = string.Empty;
    public string QuestionKey { get; private set; } = string.Empty;
    public int Times { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public DateTimeOffset FirstAskedAt { get; private set; }
    public DateTimeOffset LastAskedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private KnowledgeGap() { }

    public KnowledgeGap(Guid tenantId, Guid establishmentId, string question, string questionKey, string source, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        Question = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(question, nameof(Question)).Trim(), 500, nameof(Question));
        QuestionKey = Ensure.MaxLength(questionKey, 200, nameof(QuestionKey));
        Source = Ensure.MaxLength(source, 30, nameof(Source));
        Times = 1;
        FirstAskedAt = now;
        LastAskedAt = now;
    }

    public void AskedAgain(string question, DateTimeOffset now)
    {
        Question = Ensure.MaxLength(question.Trim(), 500, nameof(Question));
        Times++;
        LastAskedAt = now;
        ResolvedAt = null;
    }

    public void Resolve(DateTimeOffset now) => ResolvedAt = now;
}
