using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.I18n;

namespace QuroErp.Domain.Bc.Localization;

/// <summary>Tax method of a country (localization.md "국가 설정", tax.md).</summary>
public static class TaxMethod
{
    public const string SalesTax = "SALES_TAX";
    public const string Vat = "VAT";
}

/// <summary>Tax calculation unit of the company (bc_company.tax_calc_mode, BC-BR-543).</summary>
public static class TaxCalcMode
{
    public const string Document = "DOCUMENT";
    public const string Line = "LINE";

    public static IReadOnlyList<string> All { get; } = [Document, Line];
}

/// <summary>
/// Everything that differs by company country (localization.md "국가 설정"). Services pick the profile by
/// bc_company.country_code instead of branching on the country themselves.
/// </summary>
public interface ICountryProfile
{
    /// <summary>ISO 3166-1 alpha-2.</summary>
    string CountryCode { get; }

    string BaseCurrencyCode { get; }

    string DefaultLangCode { get; }

    /// <summary>TaxMethod value.</summary>
    string TaxMethod { get; }

    /// <summary>.NET date format for display, e.g. MM/dd/yyyy.</summary>
    string DateFormat { get; }

    /// <summary>Region part of the display culture, e.g. "US" for en-US.</summary>
    string CultureRegion { get; }

    /// <summary>Default company time zone, an IANA name (BC-BR-318).</summary>
    string DefaultTimeZone { get; }

    /// <summary>Default sys_decimal_setting rows: quantity decimals and rounding mode (BC-BR-422).</summary>
    IReadOnlyList<DecimalSetting> DefaultDecimalSettings { get; }

    /// <summary>bc_country_decimal rows of this country (BC-BR-422). Installed for every supported country.</summary>
    IReadOnlyList<CountryDecimal> DefaultUnitPriceDecimals { get; }

    /// <summary>TaxKind values tax codes of this country may use (BC-BR-502).</summary>
    IReadOnlyList<string> TaxKinds { get; }

    /// <summary>Tax codes installed for this country (BC-BR-505). NONE cannot be deleted or disabled.</summary>
    IReadOnlyList<DefaultTaxCode> DefaultTaxCodes { get; }
}

public sealed class UsCountryProfile : ICountryProfile
{
    public string CountryCode => CountryProfiles.Us;

    public string BaseCurrencyCode => "USD";

    public string DefaultLangCode => LanguageCode.En;

    public string TaxMethod => Localization.TaxMethod.SalesTax;

    public string DateFormat => "MM/dd/yyyy";

    public string CultureRegion => CountryProfiles.Us;

    public string DefaultTimeZone => "America/New_York";

    public IReadOnlyList<DecimalSetting> DefaultDecimalSettings { get; } =
    [
        new(DecimalModule.Mm, qtyDecimals: 2, RoundingMode.HalfUp),
        new(DecimalModule.Sd, qtyDecimals: 0, RoundingMode.HalfUp),
    ];

    public IReadOnlyList<CountryDecimal> DefaultUnitPriceDecimals { get; } =
    [
        new(CountryProfiles.Us, DecimalModule.Mm, unitPriceDecimals: 4),
        new(CountryProfiles.Us, DecimalModule.Sd, unitPriceDecimals: 2),
    ];

    public IReadOnlyList<string> TaxKinds { get; } = [TaxKind.SalesTax, TaxKind.SalesTaxExempt, TaxKind.None];

    public IReadOnlyList<DefaultTaxCode> DefaultTaxCodes { get; } =
    [
        new("EXEMPT", TaxKind.SalesTaxExempt, 0m),
        new("NONE", TaxKind.None, 0m),
    ];
}

public sealed class KrCountryProfile : ICountryProfile
{
    public string CountryCode => CountryProfiles.Kr;

    public string BaseCurrencyCode => "KRW";

    public string DefaultLangCode => LanguageCode.Ko;

    public string TaxMethod => Localization.TaxMethod.Vat;

    public string DateFormat => "yyyy-MM-dd";

    public string CultureRegion => CountryProfiles.Kr;

    public string DefaultTimeZone => "Asia/Seoul";

    public IReadOnlyList<DecimalSetting> DefaultDecimalSettings { get; } =
    [
        new(DecimalModule.Mm, qtyDecimals: 2, RoundingMode.HalfUp),
        new(DecimalModule.Sd, qtyDecimals: 0, RoundingMode.HalfUp),
    ];

    public IReadOnlyList<CountryDecimal> DefaultUnitPriceDecimals { get; } =
    [
        new(CountryProfiles.Kr, DecimalModule.Mm, unitPriceDecimals: 2),
        new(CountryProfiles.Kr, DecimalModule.Sd, unitPriceDecimals: 0),
    ];

    public IReadOnlyList<string> TaxKinds { get; } = [TaxKind.VatStandard, TaxKind.VatZero, TaxKind.VatExempt, TaxKind.None];

    public IReadOnlyList<DefaultTaxCode> DefaultTaxCodes { get; } =
    [
        new("VAT10", TaxKind.VatStandard, 10m),
        new("VAT0", TaxKind.VatZero, 0m),
        new("VATEX", TaxKind.VatExempt, 0m),
        new("NONE", TaxKind.None, 0m),
    ];
}

/// <summary>Country profiles of MVP 1.0 (US, KR). New regions add a profile here.</summary>
public static class CountryProfiles
{
    public const string Us = "US";
    public const string Kr = "KR";

    /// <summary>Unsupported country. Arg: country code.</summary>
    public const string UnsupportedCode = "BC-E-313";

    private static readonly Dictionary<string, ICountryProfile> Profiles = new ICountryProfile[]
    {
        new UsCountryProfile(),
        new KrCountryProfile(),
    }.ToDictionary(p => p.CountryCode, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Supported => Profiles.Keys;

    /// <summary>bc_country_decimal defaults of all supported countries, whatever the company country (BC-BR-422).</summary>
    public static IReadOnlyList<CountryDecimal> DefaultUnitPriceDecimals =>
        [.. Profiles.Values.OrderBy(p => p.CountryCode, StringComparer.Ordinal).SelectMany(p => p.DefaultUnitPriceDecimals)];

    /// <exception cref="DomainException">BC-E-313 when the country is not supported.</exception>
    public static ICountryProfile Get(string? countryCode) =>
        countryCode is not null && Profiles.TryGetValue(countryCode, out var profile)
            ? profile
            : throw new DomainException(UnsupportedCode, countryCode ?? "");
}
