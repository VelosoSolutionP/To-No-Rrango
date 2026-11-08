using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Media;

/// <summary>A photo uploaded by an establishment and served publicly by its id.</summary>
public sealed class MediaFile : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid EstablishmentId { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public byte[] Data { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    private MediaFile() { }

    public MediaFile(Guid tenantId, Guid establishmentId, string contentType, byte[] data, DateTimeOffset now)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        EstablishmentId = Ensure.NotEmpty(establishmentId, nameof(EstablishmentId));
        ContentType = Ensure.NotNullOrWhiteSpace(contentType, nameof(ContentType));
        Data = data;
        CreatedAt = now;
    }
}
