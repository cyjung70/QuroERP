using System.Data;

namespace QuroErp.Application.Bc.Localization;

/// <summary>Requested use_yn of a language.</summary>
public sealed record LanguageUseRow(string LangCode, bool UseYn);

/// <summary>
/// Writes of the localization setting screen (BC-UC-027): sys_language, bc_country_decimal, sys_decimal_setting and
/// bc_company.tax_calc_mode. Updates skip rows whose values do not change, so their updated_at stays.
/// </summary>
public interface ILocalizationSettingRepository
{
    /// <summary>
    /// Latest updated_at of sys_language, bc_country_decimal, sys_decimal_setting and the company row: the version PUT compares.
    /// </summary>
    /// <param name="forUpdate">Lock those rows until the transaction ends.</param>
    Task<DateTimeOffset> GetVersionAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Whether a document line of the module (MM, SD) exists.</summary>
    Task<bool> HasDocumentsAsync(string moduleCode, IDbTransaction transaction, CancellationToken cancellationToken);

    Task UpdateLanguagesAsync(IReadOnlyList<LanguageUseRow> languages, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    Task UpdateCountryDecimalsAsync(
        IReadOnlyList<CountryDecimalRow> countryDecimals, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    Task UpdateDecimalSettingsAsync(
        IReadOnlyList<DecimalSettingRow> settings, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sets tax_calc_mode of the company with the lowest id (ADR-0025).</summary>
    Task UpdateTaxCalcModeAsync(string taxCalcMode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Moves users whose language is one of fromLangCodes to toLangCode (BC-BR-403).</summary>
    /// <returns>Number of users moved.</returns>
    Task<int> MoveUserLanguagesAsync(
        IReadOnlyList<string> fromLangCodes, string toLangCode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);
}
