using FluentAssertions;
using Npgsql;
using QuroErp.Domain.Bc.Localization;

namespace QuroERP.Database.Tests;

/// <summary>Company country at installation (TASK-CORE-014, BC-BR-313). Each class migrates its own schema.</summary>
public class CompanySetupTests : IClassFixture<MigratedSchemaFixture>
{
    private readonly MigratedSchemaFixture _db;

    public CompanySetupTests(MigratedSchemaFixture db) => _db = db;

    [DatabaseFact, Trait("Id", "CORE-014-IT-20")]
    public async Task SetCountry_NoCompanyThenKrThenUs_UsesCountryProfileDefaults()
    {
        var withoutName = () => CompanySetup.SetCountryAsync(_db.ConnectionString, "KR", null, CancellationToken.None);
        await withoutName.Should().ThrowAsync<InvalidOperationException>();

        var created = await CompanySetup.SetCountryAsync(_db.ConnectionString, "KR", "Quro Korea", CancellationToken.None);

        created.Should().BeTrue();
        Company().Should().Be(("Quro Korea", "KR", "KRW", "ko"));
        Decimals().Should().Equal(("MM", 2, 2, "HALF_UP"), ("SD", 0, 0, "HALF_UP"));

        var updated = await CompanySetup.SetCountryAsync(_db.ConnectionString, "US", null, CancellationToken.None);

        updated.Should().BeFalse();
        Company().Should().Be(("Quro Korea", "US", "USD", "en"));
        Decimals().Should().Equal(("MM", 4, 2, "HALF_UP"), ("SD", 2, 0, "HALF_UP"));
    }

    [DatabaseFact, Trait("Id", "CORE-014-IT-21")]
    public async Task SetCountry_UnsupportedCountry_ThrowsBcE313()
    {
        var act = () => CompanySetup.SetCountryAsync(_db.ConnectionString, "JP", "Quro Japan", CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().StartWith("BC-E-313");
    }

    [Fact, Trait("Id", "CORE-014-T10")]
    public void Countries_MatchDomainCountryProfiles()
    {
        CompanySetup.Countries.Keys.Should().BeEquivalentTo(CountryProfiles.Supported);
        foreach (var (code, defaults) in CompanySetup.Countries)
        {
            var profile = CountryProfiles.Get(code);
            defaults.Should().BeEquivalentTo(new CountryDefaults(
                profile.CountryCode, profile.BaseCurrencyCode, profile.DefaultLangCode,
                profile.DefaultDecimalSettings
                    .Select(s => new DecimalDefaults(s.Module, s.UnitPriceDecimals, s.QtyDecimals, Rounding.ToCode(s.RoundingMode)))
                    .ToList()));
        }
    }

    private (string, string, string, string) Company()
    {
        using var connection = _db.Open();
        using var command = new NpgsqlCommand(
            "SELECT name, country_code, base_currency_code, default_lang_code FROM bc_company", connection);
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        var row = (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        reader.Read().Should().BeFalse("there is one company");
        return row;
    }

    private List<(string, int, int, string)> Decimals()
    {
        using var connection = _db.Open();
        using var command = new NpgsqlCommand(
            "SELECT module, unit_price_decimals, qty_decimals, rounding_mode FROM sys_decimal_setting ORDER BY module", connection);
        using var reader = command.ExecuteReader();
        var rows = new List<(string, int, int, string)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3)));
        }
        return rows;
    }
}

/// <summary>A schema with an exchange rate, so the company has started trading.</summary>
public class CompanySetupWithTransactionsTests : IClassFixture<MigratedSchemaFixture>
{
    private readonly MigratedSchemaFixture _db;

    public CompanySetupWithTransactionsTests(MigratedSchemaFixture db) => _db = db;

    [DatabaseFact, Trait("Id", "CORE-014-IT-22")]
    public async Task SetCountry_TransactionsExist_IsBlockedAndChangesNothing()
    {
        using (var connection = _db.Open())
        using (var command = new NpgsqlCommand(
            """
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, created_by, updated_by)
            VALUES ('Quro', 'US', 'USD', 'en', 's', 's');
            INSERT INTO bc_exchange_rate (cur_dt, created_by, updated_by) VALUES (DATE '2026-10-01', 's', 's');
            """, connection))
        {
            command.ExecuteNonQuery();
        }

        var act = () => CompanySetup.SetCountryAsync(_db.ConnectionString, "KR", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        using var check = _db.Open();
        using var query = new NpgsqlCommand("SELECT country_code FROM bc_company", check);
        query.ExecuteScalar().Should().Be("US");
    }
}
