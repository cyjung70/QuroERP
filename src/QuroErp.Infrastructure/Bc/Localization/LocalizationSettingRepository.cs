using System.Data;
using Dapper;
using QuroErp.Application.Bc.Localization;

namespace QuroErp.Infrastructure.Bc.Localization;

internal sealed class LocalizationSettingRepository : ILocalizationSettingRepository
{
    // Single company (ADR-0025): the lowest id is the company. GREATEST skips NULL (no company yet)
    private const string SelectVersion = """
        SELECT GREATEST(
            (SELECT max(updated_at) FROM sys_language),
            (SELECT max(updated_at) FROM bc_country_decimal),
            (SELECT max(updated_at) FROM sys_decimal_setting),
            (SELECT updated_at FROM bc_company ORDER BY id LIMIT 1))
        """;

    private const string SelectVersionForUpdate = """
        SELECT GREATEST(
            (SELECT max(updated_at) FROM (SELECT updated_at FROM sys_language FOR UPDATE) l),
            (SELECT max(updated_at) FROM (SELECT updated_at FROM bc_country_decimal FOR UPDATE) c),
            (SELECT max(updated_at) FROM (SELECT updated_at FROM sys_decimal_setting FOR UPDATE) d),
            (SELECT updated_at FROM (SELECT updated_at FROM bc_company ORDER BY id LIMIT 1 FOR UPDATE) b))
        """;

    // Document lines with quantities of each module (BC-BR-425). The MVP has no quotation; the sales invoice
    // (dn_no) is also the shipment and the goods receipt is also the purchase invoice. sd_production_result_line
    // is the SD simple result for sales BOM (SET) items, not a production (PP) document.
    private const string SelectMmHasDocuments = """
        SELECT EXISTS (SELECT 1 FROM mm_purchase_order_line)
            OR EXISTS (SELECT 1 FROM mm_goods_receipt_line)
            OR EXISTS (SELECT 1 FROM mm_goods_issue_line)
            OR EXISTS (SELECT 1 FROM mm_goods_return_line)
            OR EXISTS (SELECT 1 FROM mm_unplanned_receipt_line)
            OR EXISTS (SELECT 1 FROM mm_stock_count_line)
            OR EXISTS (SELECT 1 FROM mm_stock_transfer_line)
        """;

    private const string SelectSdHasDocuments = """
        SELECT EXISTS (SELECT 1 FROM sd_sales_order_line)
            OR EXISTS (SELECT 1 FROM sd_sales_invoice_line)
            OR EXISTS (SELECT 1 FROM sd_production_result_line)
        """;

    private const string UpdateLanguage = """
        UPDATE sys_language
        SET use_yn = @UseYn, updated_at = now(), updated_by = @UpdatedBy
        WHERE lang_code = @LangCode AND use_yn <> @UseYn
        """;

    private const string UpdateCountryDecimal = """
        UPDATE bc_country_decimal
        SET unit_price_decimals = @UnitPriceDecimals, updated_at = now(), updated_by = @UpdatedBy
        WHERE country_code = @CountryCode AND module = @Module AND unit_price_decimals <> @UnitPriceDecimals
        """;

    private const string UpdateDecimalSetting = """
        UPDATE sys_decimal_setting
        SET qty_decimals = @QtyDecimals, rounding_mode = @RoundingMode, updated_at = now(), updated_by = @UpdatedBy
        WHERE module = @Module AND (qty_decimals <> @QtyDecimals OR rounding_mode <> @RoundingMode)
        """;

    private const string UpdateTaxCalcMode = """
        UPDATE bc_company
        SET tax_calc_mode = @TaxCalcMode, updated_at = now(), updated_by = @UpdatedBy
        WHERE id = (SELECT min(id) FROM bc_company) AND tax_calc_mode <> @TaxCalcMode
        """;

    private const string UpdateUserLanguages = """
        UPDATE sys_app_user
        SET lang_code = @ToLangCode, updated_at = now(), updated_by = @UpdatedBy
        WHERE lang_code = ANY(@FromLangCodes)
        """;

    public async Task<DateTimeOffset> GetVersionAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        // Npgsql reads timestamptz as a UTC DateTime
        var version = await transaction.Connection!.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            forUpdate ? SelectVersionForUpdate : SelectVersion, transaction: transaction, cancellationToken: cancellationToken));
        return version is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : DateTimeOffset.UnixEpoch;
    }

    public Task<bool> HasDocumentsAsync(string moduleCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var sql = moduleCode switch
        {
            "MM" => SelectMmHasDocuments,
            "SD" => SelectSdHasDocuments,
            _ => throw new ArgumentOutOfRangeException(nameof(moduleCode), moduleCode, "Only MM and SD have decimal settings."),
        };
        return transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));
    }

    public Task UpdateLanguagesAsync(
        IReadOnlyList<LanguageUseRow> languages, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateLanguage,
            languages.Select(l => new { l.LangCode, l.UseYn, UpdatedBy = updatedBy }).ToList(),
            transaction,
            cancellationToken: cancellationToken));

    public Task UpdateCountryDecimalsAsync(
        IReadOnlyList<CountryDecimalRow> countryDecimals, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateCountryDecimal,
            countryDecimals.Select(d => new { d.CountryCode, d.Module, d.UnitPriceDecimals, UpdatedBy = updatedBy }).ToList(),
            transaction,
            cancellationToken: cancellationToken));

    public Task UpdateDecimalSettingsAsync(
        IReadOnlyList<DecimalSettingRow> settings, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateDecimalSetting,
            settings.Select(s => new { s.Module, s.QtyDecimals, s.RoundingMode, UpdatedBy = updatedBy }).ToList(),
            transaction,
            cancellationToken: cancellationToken));

    public Task UpdateTaxCalcModeAsync(string taxCalcMode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateTaxCalcMode, new { TaxCalcMode = taxCalcMode, UpdatedBy = updatedBy }, transaction, cancellationToken: cancellationToken));

    public Task<int> MoveUserLanguagesAsync(
        IReadOnlyList<string> fromLangCodes, string toLangCode, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateUserLanguages,
            new { FromLangCodes = fromLangCodes.ToArray(), ToLangCode = toLangCode, UpdatedBy = updatedBy },
            transaction,
            cancellationToken: cancellationToken));
}
