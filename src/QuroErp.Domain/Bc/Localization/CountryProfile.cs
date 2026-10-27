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

    /// <summary>Default sys_decimal_setting rows (BC-BR-422).</summary>
    IReadOnlyList<DecimalSetting> DefaultDecimalSettings { get; }
}

public sealed class UsCountryProfile : ICountryProfile
{
    public string CountryCode => CountryProfiles.Us;

    public string BaseCurrencyCode => "USD";

    public string DefaultLangCode => LanguageCode.En;

    public string TaxMethod => Localization.TaxMethod.SalesTax;

    public string DateFormat => "MM/dd/yyyy";

    public string CultureRegion => CountryProfiles.Us;

    public IReadOnlyList<DecimalSetting> DefaultDecimalSettings { get; } =
    [
        new(DecimalModule.Mm, unitPriceDecimals: 4, qtyDecimals: 2, RoundingMode.HalfUp),
        new(DecimalModule.Sd, unitPriceDecimals: 2, qtyDecimals: 0, RoundingMode.HalfUp),
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

    public IReadOnlyList<DecimalSetting> DefaultDecimalSettings { get; } =
    [
        new(DecimalModule.Mm, unitPriceDecimals: 2, qtyDecimals: 2, RoundingMode.HalfUp),
        new(DecimalModule.Sd, unitPriceDecimals: 0, qtyDecimals: 0, RoundingMode.HalfUp),
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

    /// <exception cref="DomainException">BC-E-313 when the country is not supported.</exception>
    public static ICountryProfile Get(string? countryCode) =>
        countryCode is not null && Profiles.TryGetValue(countryCode, out var profile)
            ? profile
            : throw new DomainException(UnsupportedCode, countryCode ?? "");
}
