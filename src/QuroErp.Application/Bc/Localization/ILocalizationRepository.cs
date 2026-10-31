using System.Data;

namespace QuroErp.Application.Bc.Localization;

/// <summary>Country values of the single company (bc_company, BC-BR-312~314, BC-BR-318, BC-BR-543).</summary>
public sealed record CompanyLocalizationRow(string CountryCode, string BaseCurrencyCode, string DefaultLangCode, string TaxCalcMode, string TimeZone);

/// <summary>sys_language row.</summary>
public sealed record LanguageRow(string LangCode, int SortOrder, bool UseYn);

/// <summary>bc_currency row. Id is the owner id of bc_currency_translation. CountryCode may be null (BC-BR-428).</summary>
public sealed record CurrencyRow(long Id, string CurrencyCode, int Decimals, bool UseYn, string? CountryCode);

/// <summary>sys_decimal_setting row.</summary>
public sealed record DecimalSettingRow(string Module, int QtyDecimals, string RoundingMode);

/// <summary>bc_country_decimal row.</summary>
public sealed record CountryDecimalRow(string CountryCode, string Module, int UnitPriceDecimals);

/// <summary>Localization settings (localization.md). Callers pass their own transaction.</summary>
public interface ILocalizationRepository
{
    /// <returns>The company with the lowest id (BC-BR-301), or null when there is no company yet.</returns>
    Task<CompanyLocalizationRow?> FindCompanyAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All languages in sort_order.</summary>
    Task<IReadOnlyList<LanguageRow>> FindLanguagesAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All currencies in currency_code order.</summary>
    Task<IReadOnlyList<CurrencyRow>> FindCurrenciesAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All module settings in module order.</summary>
    Task<IReadOnlyList<DecimalSettingRow>> FindDecimalSettingsAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All country unit price decimals in country_code, module order.</summary>
    Task<IReadOnlyList<CountryDecimalRow>> FindCountryDecimalsAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}
