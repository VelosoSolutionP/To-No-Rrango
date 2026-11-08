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
