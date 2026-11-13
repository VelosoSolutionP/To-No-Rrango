using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Catalog;

/// <summary>
/// A product already bought from the GTIN provider, kept in Postgres for every store: a barcode never changes
/// its product, and the plan allows only 2 calls per minute.
/// </summary>
public sealed class GtinRecord : AggregateRoot
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? Ncm { get; private set; }
    public string? Cest { get; private set; }
    public byte[]? Image { get; private set; }

    /// <summary>True once the picture was asked for, even when the provider had none, so it is not asked again.</summary>
    public bool ImageChecked { get; private set; }
    public DateTimeOffset FetchedAt { get; private set; }

    private GtinRecord() { }

    public GtinRecord(string code, string name, string? brand, string? ncm, string? cest, byte[]? image, bool imageChecked, DateTimeOffset now)
    {
        Id = GuidV7.New();
        Code = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(code, nameof(Code)), 14, nameof(Code));
        Refresh(name, brand, ncm, cest, image, imageChecked, now);
    }

    public void Refresh(string name, string? brand, string? ncm, string? cest, byte[]? image, bool imageChecked, DateTimeOffset now)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Brand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim();
        Ncm = ncm;
        Cest = cest;
        Image = image ?? Image;
        ImageChecked = ImageChecked || imageChecked;
        FetchedAt = now;
    }
}
