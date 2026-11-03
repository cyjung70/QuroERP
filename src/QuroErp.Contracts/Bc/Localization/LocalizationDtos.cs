using System.ComponentModel.DataAnnotations;

namespace QuroErp.Contracts.Bc.Localization;

/// <summary>Localization API routes (BC-UC-027).</summary>
public static class LocalizationRoutes
{
    /// <summary>GET: localization values every screen uses (BC-BR-426). Query lang selects currency names.</summary>
    public const string Base = "api/bc/localization";

    /// <summary>GET, PUT: the localization setting screen (BC-UC-027). PUT needs Full Access (BC-BR-427).</summary>
    public const string Setting = "api/bc/localization-setting";
}

/// <summary>
/// Country, base currency, languages, currency decimals and countries, country unit price decimals, module
/// decimals and tax calculation unit (BC-BR-426). Screens use it for input decimals and display formats.
/// </summary>
/// <param name="CountryCode">US or KR (BC-BR-313).</param>
/// <param name="TaxMethod">SALES_TAX or VAT, from the country.</param>
/// <param name="DateFormat">.NET date format of the country, e.g. MM/dd/yyyy.</param>
/// <param name="TaxCalcMode">DOCUMENT or LINE (BC-BR-543).</param>
/// <param name="TimeZone">IANA name of the company time zone; the web shows every time in it (BC-BR-318).</param>
public sealed record LocalizationDto(
    string CountryCode,
    string BaseCurrencyCode,
    string DefaultLangCode,
    string TimeZone,
    string TaxMethod,
    string DateFormat,
    string TaxCalcMode,
    IReadOnlyList<LanguageDto> Languages,
    IReadOnlyList<CurrencyDto> Currencies,
    IReadOnlyList<CountryDecimalDto> CountryDecimals,
    IReadOnlyList<DecimalSettingDto> DecimalSettings);

/// <summary>A language in use, in sort order.</summary>
public sealed record LanguageDto(string LangCode, int SortOrder);

/// <summary>A currency in use. Name falls back request language → company default language → en (BC-BR-402).</summary>
/// <param name="CountryCode">Country of the currency, null when it has none (BC-BR-428).</param>
public sealed record CurrencyDto(string CurrencyCode, string Name, int Decimals, string? CountryCode);

/// <summary>
/// Unit price decimals of a country and module (BC-BR-422). A document line uses the row of its currency
/// country, or the company country row when there is none (BC-BR-429).
/// </summary>
/// <param name="Module">MM or SD.</param>
public sealed record CountryDecimalDto(string CountryCode, string Module, int UnitPriceDecimals);

/// <summary>Quantity decimals and rounding mode of a module.</summary>
/// <param name="Module">MM or SD.</param>
/// <param name="RoundingMode">HALF_UP or DOWN (BC-BR-424).</param>
public sealed record DecimalSettingDto(string Module, int QtyDecimals, string RoundingMode);

/// <summary>
/// The localization setting screen (BC-UC-027): company values (read only, changed on the company screen) and the
/// values PUT changes, with every language whether in use or not.
/// </summary>
/// <param name="UpdatedAt">Latest change of the setting rows. Send it back on PUT to detect concurrent updates.</param>
public sealed record LocalizationSettingDto(
    string CountryCode,
    string BaseCurrencyCode,
    string DefaultLangCode,
    string TaxCalcMode,
    IReadOnlyList<LanguageSettingDto> Languages,
    IReadOnlyList<CountryDecimalDto> CountryDecimals,
    IReadOnlyList<DecimalSettingDto> DecimalSettings,
    DateTimeOffset UpdatedAt);

/// <summary>Language use (BC-BR-401, BC-BR-403). en is always in use.</summary>
public sealed record LanguageSettingDto([Required] string LangCode, bool UseYn);

/// <summary>
/// Changes the listed rows only; rows not listed keep their values. Reducing decimals that documents use is 409 BC-E-425 (BC-BR-425).
/// </summary>
/// <param name="TaxCalcMode">DOCUMENT or LINE (BC-BR-543).</param>
/// <param name="UpdatedAt">The UpdatedAt from the last GET. A different value means someone else saved first (409 Common-E-004).</param>
public sealed record UpdateLocalizationSettingRequest(
    [Required] IReadOnlyList<LanguageSettingDto> Languages,
    [Required] IReadOnlyList<CountryDecimalDto> CountryDecimals,
    [Required] IReadOnlyList<DecimalSettingDto> DecimalSettings,
    [Required, AllowedValues("DOCUMENT", "LINE")] string TaxCalcMode,
    DateTimeOffset UpdatedAt);
