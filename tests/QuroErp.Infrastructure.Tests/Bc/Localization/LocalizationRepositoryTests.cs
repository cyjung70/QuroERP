using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.I18n;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Localization;

/// <summary>Localization tables (0010, 0011, 0102, 0103, Seed 0504) and repository, real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class LocalizationRepositoryTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "CORE-014-IT-10")]
    public async Task Seeds_LanguagesCurrenciesAndUsDecimalSettings()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var repository = db.Get<ILocalizationRepository>();

        var languages = await repository.FindLanguagesAsync(uow.Transaction, CancellationToken.None);
        var currencies = await repository.FindCurrenciesAsync(uow.Transaction, CancellationToken.None);
        var decimals = await repository.FindDecimalSettingsAsync(uow.Transaction, CancellationToken.None);
        var countryDecimals = await repository.FindCountryDecimalsAsync(uow.Transaction, CancellationToken.None);
        var company = await repository.FindCompanyAsync(uow.Transaction, CancellationToken.None);

        languages.Should().Equal(new LanguageRow("en", 1, true), new LanguageRow("ko", 2, true), new LanguageRow("sw", 3, false));
        currencies.Select(c => (c.CurrencyCode, c.Decimals, c.UseYn, c.CountryCode)).Should().Equal(("KRW", 0, true, "KR"), ("USD", 2, true, "US"));
        decimals.Should().Equal(new DecimalSettingRow("MM", 2, "HALF_UP"), new DecimalSettingRow("SD", 0, "HALF_UP"));
        countryDecimals.Should().Equal(
            new CountryDecimalRow("KR", "MM", 2), new CountryDecimalRow("KR", "SD", 0),
            new CountryDecimalRow("US", "MM", 4), new CountryDecimalRow("US", "SD", 2));
        company.Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "CORE-014-IT-11")]
    public async Task FindCompanyAsync_ReturnsLowestIdCountryValues_AndCurrencyNamesFallBack()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var repository = db.Get<ILocalizationRepository>();
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO bc_company (id, name, country_code, base_currency_code, default_lang_code, created_by, updated_by)
            VALUES (1, 'Quro', 'KR', 'KRW', 'ko', 's', 's'), (2, 'Other', 'US', 'USD', 'en', 's', 's')
            """,
            transaction: uow.Transaction);

        var company = await repository.FindCompanyAsync(uow.Transaction, CancellationToken.None);
        var currencies = await repository.FindCurrenciesAsync(uow.Transaction, CancellationToken.None);
        var names = await new TranslationService(db.Get<ITranslationRepository>(), repository).GetNamesAsync(
            TranslationTarget.CurrencyName, currencies.Select(c => c.Id).ToList(), "sw", uow.Transaction, CancellationToken.None);

        company.Should().Be(new CompanyLocalizationRow("KR", "KRW", "ko", "DOCUMENT"));
        names.Values.Should().BeEquivalentTo("대한민국 원", "미국 달러");
    }

    [DatabaseTheory, Trait("Id", "CORE-014-IT-12")]
    [InlineData("us", "USD", "en", "DOCUMENT", PostgresErrorCodes.CheckViolation)]
    [InlineData("US", "USD", "en", "ROW", PostgresErrorCodes.CheckViolation)]
    [InlineData("US", "EUR", "en", "DOCUMENT", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("US", "USD", "ja", "DOCUMENT", PostgresErrorCodes.ForeignKeyViolation)]
    public async Task CompanyConstraints_RejectInvalidCountryValues(
        string countryCode, string baseCurrencyCode, string defaultLangCode, string taxCalcMode, string sqlState)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var act = () => uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, tax_calc_mode, created_by, updated_by)
            VALUES ('Quro', @CountryCode, @BaseCurrencyCode, @DefaultLangCode, @TaxCalcMode, 's', 's')
            """,
            new { CountryCode = countryCode, BaseCurrencyCode = baseCurrencyCode, DefaultLangCode = defaultLangCode, TaxCalcMode = taxCalcMode },
            uow.Transaction);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(sqlState);
    }

    [DatabaseTheory, Trait("Id", "CORE-014-IT-13")]
    [InlineData("UPDATE sys_language SET use_yn = false WHERE lang_code = 'en'")]
    [InlineData("INSERT INTO sys_language (lang_code, sort_order, created_by, updated_by) VALUES ('EN-us', 9, 's', 's')")]
    [InlineData("INSERT INTO bc_currency (currency_code, decimals, created_by, updated_by) VALUES ('EUR', 5, 's', 's')")]
    [InlineData("UPDATE bc_country_decimal SET unit_price_decimals = 7 WHERE country_code = 'US' AND module = 'MM'")]
    [InlineData("INSERT INTO bc_country_decimal (country_code, module, unit_price_decimals, created_by, updated_by) VALUES ('jp', 'SD', 0, 's', 's')")]
    [InlineData("UPDATE bc_currency SET country_code = 'usa' WHERE currency_code = 'USD'")]
    [InlineData("UPDATE sys_decimal_setting SET qty_decimals = 5 WHERE module = 'SD'")]
    [InlineData("UPDATE sys_decimal_setting SET rounding_mode = 'HALF_EVEN' WHERE module = 'SD'")]
    public async Task LocalizationConstraints_RejectOutOfRuleValues(string sql)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var act = () => uow.Transaction.Connection!.ExecuteAsync(sql, transaction: uow.Transaction);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [DatabaseFact, Trait("Id", "CORE-014-IT-14")]
    public async Task AppUserLangCode_ReferencesLanguageMasterInsteadOfKoEnCheck()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var constraints = (await uow.Transaction.Connection!.QueryAsync<(string Name, string Referenced)>(
            """
            SELECT conname, coalesce(confrelid::regclass::text, '')
            FROM pg_constraint
            WHERE conrelid = 'sys_app_user'::regclass AND conname LIKE '%lang_code'
            """,
            transaction: uow.Transaction)).ToList();

        constraints.Should().Equal(("fk_sys_app_user_lang_code", "sys_language"));
    }
}
