using System.Data;

namespace QuroErp.Application.Bc.Localization;

/// <summary>Country values of the single company (bc_company, BC-BR-312~314, BC-BR-543).</summary>
public sealed record CompanyLocalizationRow(string CountryCode, string BaseCurrencyCode, string DefaultLangCode, string TaxCalcMode);

/// <summary>sys_language row.</summary>
public sealed record LanguageRow(string LangCode, int SortOrder, bool UseYn);

/// <summary>bc_currency row. Id is the owner id of bc_currency_translation.</summary>
public sealed record CurrencyRow(long Id, string CurrencyCode, int Decimals, bool UseYn);

/// <summary>sys_decimal_setting row.</summary>
public sealed record DecimalSettingRow(string Module, int UnitPriceDecimals, int QtyDecimals, string RoundingMode);

/// <summary>Localization settings (localization.md). Callers pass their own transaction.</summary>
public interface ILocalizationRepository
{
    /// <returns>The company with the lowest id (ADR-0025), or null when there is no company yet.</returns>
    Task<CompanyLocalizationRow?> FindCompanyAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All languages in sort_order.</summary>
    Task<IReadOnlyList<LanguageRow>> FindLanguagesAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All currencies in currency_code order.</summary>
    Task<IReadOnlyList<CurrencyRow>> FindCurrenciesAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All module settings in module order.</summary>
    Task<IReadOnlyList<DecimalSettingRow>> FindDecimalSettingsAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}
