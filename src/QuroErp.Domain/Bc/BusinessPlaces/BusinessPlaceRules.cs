using QuroErp.Domain.Bc.Taxes;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.BusinessPlaces;

/// <summary>Business place values the company screen saves (bc_business_place). Blank text is null. Code and default flag are not changed.</summary>
public sealed record BusinessPlaceFields(
    string? BusinessPlaceName, string? BusinessRegNo, string? SubBusinessNo, string? CeoName, string? BusinessType, string? BusinessItem,
    string? Addr1, string? Addr2, string? City, string? State, string? ZipCode, string? TelNo, bool UseYn);

/// <summary>Business place rules BC-BR-319 and BC-BR-320 (docs/architecture/organization.md).</summary>
public static class BusinessPlaceRules
{
    /// <summary>A business place cannot be added (MVP has one), or the default one cannot be turned off. No args.</summary>
    public const string SinglePlaceCode = "BC-E-319";

    /// <summary>A field missing, too long or invalid (KR registration number, sub-business number). Arg: property name.</summary>
    public const string InvalidFieldCode = "BC-E-320";

    /// <summary>The business place of a document or warehouse does not exist (400). No args.</summary>
    public const string NotFoundCode = "BC-E-321";

    public const string DefaultCode = "MAIN";
    public const string Korea = "KR";

    /// <summary>BC-BR-319: the MVP keeps one business place.</summary>
    public const int MaxPlaces = 1;

    public const int SubBusinessNoLength = 4;

    // The specification states no lengths; these follow the company fields (BC-BR-303)
    private static readonly (string Field, Func<BusinessPlaceFields, string?> Value, int MaxLength)[] Lengths =
    [
        (nameof(BusinessPlaceFields.BusinessPlaceName), f => f.BusinessPlaceName, 40),
        (nameof(BusinessPlaceFields.BusinessRegNo), f => f.BusinessRegNo, 30),
        (nameof(BusinessPlaceFields.CeoName), f => f.CeoName, 30),
        (nameof(BusinessPlaceFields.BusinessType), f => f.BusinessType, 50),
        (nameof(BusinessPlaceFields.BusinessItem), f => f.BusinessItem, 50),
        (nameof(BusinessPlaceFields.Addr1), f => f.Addr1, 100),
        (nameof(BusinessPlaceFields.Addr2), f => f.Addr2, 100),
        (nameof(BusinessPlaceFields.City), f => f.City, 50),
        (nameof(BusinessPlaceFields.State), f => f.State, 50),
        (nameof(BusinessPlaceFields.ZipCode), f => f.ZipCode, 10),
        (nameof(BusinessPlaceFields.TelNo), f => f.TelNo, 20),
    ];

    /// <summary>
    /// Trims text (blank → null), then checks the name, lengths and sub-business number. When the company country is KR the
    /// registration number is required and stored as 10 digits without hyphens (BC-BR-320, BC-BR-521).
    /// </summary>
    /// <param name="isDefault">The default place stays in use (BC-BR-319).</param>
    /// <exception cref="DomainException">BC-E-320 with the property name; BC-E-319 when the default place is turned off.</exception>
    public static BusinessPlaceFields Normalize(BusinessPlaceFields fields, string? countryCode, bool isDefault)
    {
        var normalized = new BusinessPlaceFields(
            Clean(fields.BusinessPlaceName), Clean(fields.BusinessRegNo), Clean(fields.SubBusinessNo), Clean(fields.CeoName),
            Clean(fields.BusinessType), Clean(fields.BusinessItem), Clean(fields.Addr1), Clean(fields.Addr2), Clean(fields.City),
            Clean(fields.State), Clean(fields.ZipCode), Clean(fields.TelNo), fields.UseYn);

        if (normalized.BusinessPlaceName is null)
        {
            throw new DomainException(InvalidFieldCode, nameof(BusinessPlaceFields.BusinessPlaceName));
        }
        foreach (var (field, value, maxLength) in Lengths)
        {
            if (value(normalized)?.Length > maxLength)
            {
                throw new DomainException(InvalidFieldCode, field);
            }
        }
        if (normalized.SubBusinessNo is { } sub && (sub.Length != SubBusinessNoLength || !sub.All(char.IsAsciiDigit)))
        {
            throw new DomainException(InvalidFieldCode, nameof(BusinessPlaceFields.SubBusinessNo));
        }
        if (countryCode == Korea)
        {
            var regNo = KrBusinessNumber.Normalize(normalized.BusinessRegNo)
                ?? throw new DomainException(InvalidFieldCode, nameof(BusinessPlaceFields.BusinessRegNo));
            normalized = normalized with { BusinessRegNo = regNo };
        }
        if (isDefault && !normalized.UseYn)
        {
            throw new DomainException(SinglePlaceCode);
        }
        return normalized;
    }

    /// <summary>BC-BR-319: a new business place is refused while the MVP keeps one.</summary>
    /// <exception cref="DomainException">BC-E-319.</exception>
    public static void EnsureCanAdd(int existingCount)
    {
        if (existingCount >= MaxPlaces)
        {
            throw new DomainException(SinglePlaceCode);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
