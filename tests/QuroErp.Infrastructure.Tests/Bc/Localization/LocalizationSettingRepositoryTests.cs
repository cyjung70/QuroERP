using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Localization;

/// <summary>Localization setting writes (BC-UC-027) on real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class LocalizationSettingRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "BC-UC-027-IT-01")]
    public async Task GetVersion_IsLatestUpdatedAtOfSettingRows_WithAndWithoutLock()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ILocalizationSettingRepository>();
        await uow.Transaction.Connection!.ExecuteAsync(
            "UPDATE sys_decimal_setting SET updated_at = '2099-01-01T00:00:00Z' WHERE module = 'SD'", transaction: uow.Transaction);

        var version = await repository.GetVersionAsync(forUpdate: false, uow.Transaction, None);
        var locked = await repository.GetVersionAsync(forUpdate: true, uow.Transaction, None);

        version.Should().Be(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        locked.Should().Be(version);
    }

    [DatabaseFact, Trait("Id", "BC-UC-027-IT-02")]
    public async Task HasDocuments_TrueOnlyForModuleWithDocumentLines()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ILocalizationSettingRepository>();
        var before = await repository.HasDocumentsAsync("MM", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            WITH po AS (
                INSERT INTO mm_purchase_order (po_no, currency_code, exchange_rate, business_place_id, created_by, updated_by)
                VALUES ('PO-IT', 'USD', 1, (SELECT id FROM bc_business_place WHERE is_default), 's', 's') RETURNING id)
            INSERT INTO mm_purchase_order_line (purchase_order_id, seq, tax_code_id, created_by, updated_by)
            SELECT id, 1, (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), 's', 's' FROM po
            """,
            transaction: uow.Transaction);

        var mm = await repository.HasDocumentsAsync("MM", uow.Transaction, None);
        var sd = await repository.HasDocumentsAsync("SD", uow.Transaction, None);

        before.Should().BeFalse();
        mm.Should().BeTrue();
        sd.Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "BC-UC-027-IT-03")]
    public async Task Updates_ChangeOnlyRowsWithNewValues()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ILocalizationSettingRepository>();
        var localization = db.Get<ILocalizationRepository>();
        var connection = uow.Transaction.Connection!;
        await connection.ExecuteAsync(
            """
            UPDATE sys_language SET updated_at = 'epoch', updated_by = 'old';
            UPDATE bc_country_decimal SET updated_at = 'epoch', updated_by = 'old';
            UPDATE sys_decimal_setting SET updated_at = 'epoch', updated_by = 'old';
            INSERT INTO bc_company (id, name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            VALUES (1, 'Quro', 'KR', 'KRW', 'ko', 'Asia/Seoul', 's', 'old');
            """,
            transaction: uow.Transaction);

        await repository.UpdateLanguagesAsync([new("en", true), new("sw", true)], "admin", uow.Transaction, None);
        await repository.UpdateCountryDecimalsAsync([new("US", "SD", 2), new("KR", "SD", 1)], "admin", uow.Transaction, None);
        await repository.UpdateDecimalSettingsAsync([new("MM", 2, "HALF_UP"), new("SD", 0, "DOWN")], "admin", uow.Transaction, None);
        await repository.UpdateTaxCalcModeAsync("LINE", "admin", uow.Transaction, None);

        var changed = (await connection.QueryAsync<string>(
            """
            SELECT 'lang ' || lang_code FROM sys_language WHERE updated_by = 'admin'
            UNION ALL SELECT 'country ' || country_code || ' ' || module FROM bc_country_decimal WHERE updated_by = 'admin'
            UNION ALL SELECT 'module ' || module FROM sys_decimal_setting WHERE updated_by = 'admin'
            UNION ALL SELECT 'company' FROM bc_company WHERE updated_by = 'admin'
            ORDER BY 1
            """,
            transaction: uow.Transaction)).ToList();
        changed.Should().Equal("company", "country KR SD", "lang sw", "module SD");
        (await localization.FindCompanyAsync(uow.Transaction, None))!.TaxCalcMode.Should().Be("LINE");
        (await localization.FindCountryDecimalsAsync(uow.Transaction, None)).Should().Contain(new CountryDecimalRow("KR", "SD", 1));
        (await localization.FindDecimalSettingsAsync(uow.Transaction, None)).Should().Contain(new DecimalSettingRow("SD", 0, "DOWN"));
        (await repository.GetVersionAsync(forUpdate: false, uow.Transaction, None)).Should().BeAfter(DateTimeOffset.UnixEpoch);
    }

    [DatabaseFact, Trait("Id", "BC-UC-027-IT-04")]
    public async Task MoveUserLanguages_MovesOnlyUsersOfThoseLanguages()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<ILocalizationSettingRepository>();
        var connection = uow.Transaction.Connection!;
        await connection.ExecuteAsync(
            """
            UPDATE sys_language SET use_yn = true WHERE lang_code = 'sw';
            INSERT INTO sys_app_user (userid, password_hash, lang_code, created_by, updated_by)
            VALUES ('it-ko', 'x', 'ko', 's', 's'), ('it-sw', 'x', 'sw', 's', 's'), ('it-en', 'x', 'en', 's', 's'), ('it-none', 'x', NULL, 's', 's');
            """,
            transaction: uow.Transaction);

        var moved = await repository.MoveUserLanguagesAsync(["ko", "sw"], "en", "admin", uow.Transaction, None);

        moved.Should().Be(2);
        var users = (await connection.QueryAsync<(string UserId, string? LangCode, string UpdatedBy)>(
            "SELECT userid, lang_code, updated_by FROM sys_app_user WHERE userid LIKE 'it-%' ORDER BY userid", transaction: uow.Transaction)).ToList();
        users.Should().Equal(("it-en", "en", "s"), ("it-ko", "en", "admin"), ("it-none", null, "s"), ("it-sw", "en", "admin"));
    }
}
