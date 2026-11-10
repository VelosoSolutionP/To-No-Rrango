using System.Globalization;
using System.Text;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Establishments;

public sealed class Establishment : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? CoverUrl { get; private set; }
    public string? Phone { get; private set; }
    public string? WhatsApp { get; private set; }
    public string? AddressLine { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }
    public string TimeZone { get; private set; } = "America/Sao_Paulo";
    public bool IsActive { get; private set; } = true;
    public bool IsPickupEnabled { get; private set; } = true;
    public bool IsDeliveryEnabled { get; private set; } = true;
    public bool IsDineInEnabled { get; private set; } = true;
    public long DeliveryFeeInCents { get; private set; }
    public long MinimumOrderInCents { get; private set; }
    public string PrimaryColor { get; private set; } = "#F97316";
    public string SecondaryColor { get; private set; } = "#7C3AED";

    /// <summary>Weekly hours in the <see cref="OpeningSchedule"/> text form. Null means always open.</summary>
    public string? OpeningHours { get; private set; }

    /// <summary>When closed, orders still come in, wait for the store to confirm them and are scheduled for the next opening.</summary>
    public bool AcceptsOrdersWhenClosed { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Establishment() { }

    public Establishment(
        Guid tenantId,
        string name,
        string slug,
        DateTimeOffset now,
        string? description = null,
        string? phone = null,
        string? whatsApp = null,
        string? addressLine = null,
        string? city = null,
        string? state = null,
        string? postalCode = null,
        string? logoUrl = null,
        string? coverUrl = null)
    {
        Id = GuidV7.New();
        TenantId = Ensure.NotEmpty(tenantId, nameof(TenantId));
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Slug = NormalizeSlug(slug);
        Description = NormalizeOptional(description, 500, nameof(Description));
        Phone = NormalizeOptional(phone, 30, nameof(Phone));
        WhatsApp = NormalizeOptional(whatsApp, 30, nameof(WhatsApp));
        AddressLine = NormalizeOptional(addressLine, 200, nameof(AddressLine));
        City = NormalizeOptional(city, 80, nameof(City));
        State = NormalizeOptional(state, 2, nameof(State))?.ToUpperInvariant();
        PostalCode = NormalizeOptional(postalCode, 10, nameof(PostalCode));
        LogoUrl = NormalizeOptional(logoUrl, 500, nameof(LogoUrl));
        CoverUrl = NormalizeOptional(coverUrl, 500, nameof(CoverUrl));
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void UpdateProfile(
        string name,
        string? description,
        string? phone,
        string? whatsApp,
        string? addressLine,
        string? city,
        string? state,
        string? postalCode,
        string timeZone,
        DateTimeOffset now)
    {
        Name = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(name, nameof(Name)), 120, nameof(Name));
        Description = NormalizeOptional(description, 500, nameof(Description));
        Phone = NormalizeOptional(phone, 30, nameof(Phone));
        WhatsApp = NormalizeOptional(whatsApp, 30, nameof(WhatsApp));
        AddressLine = NormalizeOptional(addressLine, 200, nameof(AddressLine));
        City = NormalizeOptional(city, 80, nameof(City));
        State = NormalizeOptional(state, 2, nameof(State))?.ToUpperInvariant();
        PostalCode = NormalizeOptional(postalCode, 10, nameof(PostalCode));
        TimeZone = Ensure.MaxLength(Ensure.NotNullOrWhiteSpace(timeZone, nameof(TimeZone)), 80, nameof(TimeZone));
        UpdatedAt = now;
    }

    public void SetBranding(string primaryColor, string secondaryColor, string? logoUrl, string? coverUrl, DateTimeOffset now)
    {
        PrimaryColor = NormalizeColor(primaryColor, nameof(PrimaryColor));
        SecondaryColor = NormalizeColor(secondaryColor, nameof(SecondaryColor));
        LogoUrl = NormalizeOptional(logoUrl, 500, nameof(LogoUrl));
        CoverUrl = NormalizeOptional(coverUrl, 500, nameof(CoverUrl));
        UpdatedAt = now;
    }

    public void SetFulfillment(
        bool pickupEnabled,
        bool deliveryEnabled,
        bool dineInEnabled,
        long deliveryFeeInCents,
        long minimumOrderInCents,
        DateTimeOffset now)
    {
        if (!pickupEnabled && !deliveryEnabled && !dineInEnabled)
        {
            throw new ArgumentException("At least one fulfillment mode must be enabled.", nameof(pickupEnabled));
        }

        IsPickupEnabled = pickupEnabled;
        IsDeliveryEnabled = deliveryEnabled;
        IsDineInEnabled = dineInEnabled;
        DeliveryFeeInCents = Ensure.NotNegative(deliveryFeeInCents, nameof(DeliveryFeeInCents));
        MinimumOrderInCents = Ensure.NotNegative(minimumOrderInCents, nameof(MinimumOrderInCents));
        UpdatedAt = now;
    }

    public void SetOpeningHours(string? openingHours, bool acceptsOrdersWhenClosed, DateTimeOffset now)
    {
        OpeningHours = OpeningSchedule.Normalize(openingHours);
        AcceptsOrdersWhenClosed = acceptsOrdersWhenClosed;
        UpdatedAt = now;
    }

    public TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
    }

    public bool IsOpenAt(DateTimeOffset utcNow) => OpeningSchedule.IsOpen(OpeningHours, utcNow, ResolveTimeZone());

    public DateTimeOffset? NextOpeningAfter(DateTimeOffset utcNow) => OpeningSchedule.NextOpening(OpeningHours, utcNow, ResolveTimeZone());

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public static string NormalizeSlug(string value)
    {
        var input = Ensure.NotNullOrWhiteSpace(value, "slug").ToLowerInvariant();
        var builder = new StringBuilder(input.Length);

        foreach (var character in input)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var normalized = RemoveDiacritics(character);
            builder.Append(char.IsLetterOrDigit(normalized) ? normalized : '-');
        }

        var slug = builder.ToString();

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');

        if (slug.Length == 0)
        {
            throw new ArgumentException("Slug must contain at least one letter or digit.", "slug");
        }

        return Ensure.MaxLength(slug, 80, "slug");
    }

    public static char RemoveDiacritics(char character) => character switch
    {
        'á' or 'à' or 'â' or 'ã' or 'ä' or 'å' => 'a',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'ó' or 'ò' or 'ô' or 'õ' or 'ö' => 'o',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'ç' => 'c',
        'ñ' => 'n',
        'ý' or 'ÿ' => 'y',
        _ => character
    };

    private static string NormalizeColor(string value, string paramName)
    {
        var color = Ensure.NotNullOrWhiteSpace(value, paramName).ToUpperInvariant();

        if (color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"{paramName} must be a hexadecimal color like #F97316.", paramName);
        }

        return color;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Ensure.MaxLength(value.Trim(), maxLength, paramName);
    }
}
