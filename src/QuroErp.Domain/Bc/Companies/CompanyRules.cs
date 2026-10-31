using QuroErp.Domain.Bc.Taxes;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Companies;

/// <summary>Company values the company screen saves (bc_company). Blank text is null.</summary>
public sealed record CompanyFields(
    string? Name, string? Billto, string? Shipto, string? Contact, string? TelNo, string? FaxNo, string? RegisterNo, string? Represent,
    string? Email, string? BusinessType, string? BusinessItem, string? CountryCode, string? BaseCurrencyCode, string? DefaultLangCode,
    string? TimeZone, string? BillingMode);

/// <summary>Company rules BC-BR-302, 303, 313, 315, 316 and 318. Master data checks (currency, language) and trading checks are the caller's.</summary>
public static class CompanyRules
{
    /// <summary>Name missing or too long. Arg: property name.</summary>
    public const string InvalidNameCode = "BC-E-302";

    /// <summary>A text field too long or a bad email. Arg: property name.</summary>
    public const string InvalidFieldCode = "BC-E-303";

    /// <summary>Base currency not in use (422) or changed after trading started (409). Arg: currency code.</summary>
    public const string BaseCurrencyCode = "BC-E-312";

    /// <summary>Country not supported (400) or changed after trading started (409). Arg: country code.</summary>
    public const string CountryCode = "BC-E-313";

    /// <summary>Default language not in use. Arg: language code.</summary>
    public const string DefaultLanguageCode = "BC-E-314";

    /// <summary>KR register number missing or invalid. Arg: property name.</summary>
    public const string RegisterNoCode = "BC-E-315";

    /// <summary>Unknown billing mode (422) or unbilled shipments block WITH_SHIPMENT (409). Arg: billing mode.</summary>
    public const string BillingModeCode = "BC-E-316";

    /// <summary>Time zone is not a known IANA name (400). Arg: time zone.</summary>
    public const string TimeZoneCode = "BC-E-318";

    public const string WithShipment = "WITH_SHIPMENT";
    public const string Separate = "SEPARATE";
    public const string Korea = "KR";

    public const int NameMaxLength = 40;

    /// <summary>BC-BR-313: MVP countries.</summary>
    public static readonly IReadOnlySet<string> Countries = new HashSet<string>(StringComparer.Ordinal) { "US", Korea };

    // BC-BR-303 lengths; business type and item allow 50 (decided for BC-BR-315, which states no limit)
    private static readonly (string Field, Func<CompanyFields, string?> Value, int MaxLength)[] Lengths =
    [
        (nameof(CompanyFields.Billto), f => f.Billto, 200),
        (nameof(CompanyFields.Shipto), f => f.Shipto, 200),
        (nameof(CompanyFields.Contact), f => f.Contact, 40),
        (nameof(CompanyFields.TelNo), f => f.TelNo, 20),
        (nameof(CompanyFields.FaxNo), f => f.FaxNo, 20),
        (nameof(CompanyFields.RegisterNo), f => f.RegisterNo, 30),
        (nameof(CompanyFields.Represent), f => f.Represent, 30),
        (nameof(CompanyFields.Email), f => f.Email, 30),
        (nameof(CompanyFields.BusinessType), f => f.BusinessType, 50),
        (nameof(CompanyFields.BusinessItem), f => f.BusinessItem, 50),
    ];

    /// <summary>
    /// Trims text (blank → null) and upper-cases the codes, then checks name, lengths, email, billing mode and the KR register number.
    /// A KR register number is stored as 10 digits without hyphens (BC-BR-521). The country itself is checked with <see cref="Countries"/>.
    /// </summary>
    /// <exception cref="DomainException">BC-E-302, 303 or 315 with the property name; BC-E-316 with the billing mode.</exception>
    public static CompanyFields Normalize(CompanyFields fields)
    {
        var normalized = new CompanyFields(
            Clean(fields.Name), Clean(fields.Billto), Clean(fields.Shipto), Clean(fields.Contact), Clean(fields.TelNo), Clean(fields.FaxNo),
            Clean(fields.RegisterNo), Clean(fields.Represent), Clean(fields.Email), Clean(fields.BusinessType), Clean(fields.BusinessItem),
            Clean(fields.CountryCode)?.ToUpperInvariant(), Clean(fields.BaseCurrencyCode)?.ToUpperInvariant(),
            Clean(fields.DefaultLangCode)?.ToLowerInvariant(), Clean(fields.TimeZone), Clean(fields.BillingMode)?.ToUpperInvariant() ?? WithShipment);

        if (normalized.Name is null || normalized.Name.Length > NameMaxLength)
        {
            throw new DomainException(InvalidNameCode, nameof(CompanyFields.Name));
        }
        foreach (var (field, value, maxLength) in Lengths)
        {
            if (value(normalized)?.Length > maxLength)
            {
                throw new DomainException(InvalidFieldCode, field);
            }
        }
        if (normalized.Email is { } email && !EmailAddress.IsValid(email))
        {
            throw new DomainException(InvalidFieldCode, nameof(CompanyFields.Email));
        }
        if (normalized.BillingMode is not (WithShipment or Separate))
        {
            throw new DomainException(BillingModeCode, normalized.BillingMode ?? "");
        }
        if (normalized.CountryCode == Korea)
        {
            // BC-BR-315: required for KR tax invoices
            var registerNo = KrBusinessNumber.Normalize(normalized.RegisterNo)
                ?? throw new DomainException(RegisterNoCode, nameof(CompanyFields.RegisterNo));
            normalized = normalized with { RegisterNo = registerNo };
        }
        return normalized;
    }

    /// <summary>
    /// BC-BR-318: an IANA time zone name the system knows, e.g. America/New_York. Windows names such as
    /// "Korea Standard Time" are not IANA names and are refused.
    /// </summary>
    public static bool IsKnownTimeZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
        && zone.HasIanaId;

    /// <summary>BC-BR-316: going back to billing with the shipment needs every shipment line billed.</summary>
    public static bool NeedsAllShipmentsBilled(string currentBillingMode, string newBillingMode) =>
        currentBillingMode == Separate && newBillingMode == WithShipment;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
