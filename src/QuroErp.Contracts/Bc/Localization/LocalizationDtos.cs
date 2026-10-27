namespace QuroErp.Contracts.Bc.Localization;

/// <summary>Localization API routes (BC-UC-027).</summary>
public static class LocalizationRoutes
{
    /// <summary>GET: localization values every screen uses (BC-BR-426). Query lang selects currency names.</summary>
    public const string Base = "api/bc/localization";
}

/// <summary>
/// Country, base currency, languages, currency decimals, module decimals and tax calculation unit (BC-BR-426).
/// Screens use it for input decimals and display formats.
/// </summary>
/// <param name="CountryCode">US or KR (BC-BR-313).</param>
/// <param name="TaxMethod">SALES_TAX or VAT, from the country.</param>
/// <param name="DateFormat">.NET date format of the country, e.g. MM/dd/yyyy.</param>
/// <param name="TaxCalcMode">DOCUMENT or LINE (BC-BR-543).</param>
public sealed record LocalizationDto(
    string CountryCode,
    string BaseCurrencyCode,
    string DefaultLangCode,
    string TaxMethod,
    string DateFormat,
    string TaxCalcMode,
    IReadOnlyList<LanguageDto> Languages,
    IReadOnlyList<CurrencyDto> Currencies,
    IReadOnlyList<DecimalSettingDto> DecimalSettings);

/// <summary>A language in use, in sort order.</summary>
public sealed record LanguageDto(string LangCode, int SortOrder);

/// <summary>A currency in use. Name falls back request language → company default language → en (BC-BR-402).</summary>
public sealed record CurrencyDto(string CurrencyCode, string Name, int Decimals);

/// <param name="Module">MM or SD.</param>
/// <param name="RoundingMode">HALF_UP or DOWN (BC-BR-424).</param>
public sealed record DecimalSettingDto(string Module, int UnitPriceDecimals, int QtyDecimals, string RoundingMode);
