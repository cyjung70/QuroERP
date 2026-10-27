using System.Data;
using Dapper;
using QuroErp.Application.Bc.Localization;

namespace QuroErp.Infrastructure.Bc.Localization;

internal sealed class LocalizationRepository : ILocalizationRepository
{
    // Single company (ADR-0025): the lowest id is the company
    private const string SelectCompany = """
        SELECT country_code, base_currency_code, default_lang_code, tax_calc_mode
        FROM bc_company
        ORDER BY id
        LIMIT 1
        """;

    private const string SelectLanguages = """
        SELECT lang_code, sort_order, use_yn FROM sys_language ORDER BY sort_order, lang_code
        """;

    private const string SelectCurrencies = """
        SELECT id, currency_code, decimals, use_yn FROM bc_currency ORDER BY currency_code
        """;

    private const string SelectDecimalSettings = """
        SELECT module, unit_price_decimals, qty_decimals, rounding_mode FROM sys_decimal_setting ORDER BY module
        """;

    public Task<CompanyLocalizationRow?> FindCompanyAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<CompanyLocalizationRow>(
            new CommandDefinition(SelectCompany, transaction: transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<LanguageRow>> FindLanguagesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<LanguageRow>(
            new CommandDefinition(SelectLanguages, transaction: transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<CurrencyRow>> FindCurrenciesAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<CurrencyRow>(
            new CommandDefinition(SelectCurrencies, transaction: transaction, cancellationToken: cancellationToken))).AsList();

    public async Task<IReadOnlyList<DecimalSettingRow>> FindDecimalSettingsAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<DecimalSettingRow>(
            new CommandDefinition(SelectDecimalSettings, transaction: transaction, cancellationToken: cancellationToken))).AsList();
}
