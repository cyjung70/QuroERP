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
        var withoutName = () => CompanySetup.SetCountryAsync(_db.ConnectionString, "KR", null, null, CancellationToken.None);
        await withoutName.Should().ThrowAsync<InvalidOperationException>();

        var created = await CompanySetup.SetCountryAsync(_db.ConnectionString, "KR", "Quro Korea", null, CancellationToken.None);

        created.Should().BeTrue();
        Company().Should().Be(("Quro Korea", "KR", "KRW", "ko"));
        // BC-BR-319: the default business place made by the schema script takes the company name
        BusinessPlaceName().Should().Be("Quro Korea");
        TimeZone().Should().Be("Asia/Seoul");
        Decimals().Should().Equal(("MM", 2, "HALF_UP"), ("SD", 0, "HALF_UP"));

        var updated = await CompanySetup.SetCountryAsync(_db.ConnectionString, "US", null, null, CancellationToken.None);

        updated.Should().BeFalse();
        Company().Should().Be(("Quro Korea", "US", "USD", "en"));
        TimeZone().Should().Be("America/New_York");
        Decimals().Should().Equal(("MM", 2, "HALF_UP"), ("SD", 0, "HALF_UP"));

        // --time-zone overrides the country default (TASK-CORE-024)
        await CompanySetup.SetCountryAsync(_db.ConnectionString, "US", null, " America/Chicago ", CancellationToken.None);

        TimeZone().Should().Be("America/Chicago");
    }

    [DatabaseFact, Trait("Id", "CORE-014-IT-21")]
    public async Task SetCountry_UnsupportedCountry_ThrowsBcE313()
    {
        var act = () => CompanySetup.SetCountryAsync(_db.ConnectionString, "JP", "Quro Japan", null, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().StartWith("BC-E-313");
    }

    [Theory, Trait("Id", "CORE-024-IT-03"), Trait("Rule", "BC-BR-318")]
    [InlineData("Mars/Olympus")]
    [InlineData("Korea Standard Time")]
    public async Task SetCountry_UnknownTimeZone_ThrowsBcE318(string timeZone)
    {
        var act = () => CompanySetup.SetCountryAsync("Host=unused", "KR", "Quro Korea", timeZone, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        thrown.Message.Should().StartWith("BC-E-318");
        thrown.ParamName.Should().Be("timeZone");
    }

    [Fact, Trait("Id", "CORE-014-T10")]
    public void Countries_MatchDomainCountryProfiles()
    {
        CompanySetup.Countries.Keys.Should().BeEquivalentTo(CountryProfiles.Supported);
        foreach (var (code, defaults) in CompanySetup.Countries)
        {
            var profile = CountryProfiles.Get(code);
            defaults.Should().BeEquivalentTo(new CountryDefaults(
                profile.CountryCode, profile.BaseCurrencyCode, profile.DefaultLangCode, profile.DefaultTimeZone,
                profile.DefaultDecimalSettings
                    .Select(s => new DecimalDefaults(s.Module, s.QtyDecimals, Rounding.ToCode(s.RoundingMode)))
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

    private string BusinessPlaceName()
    {
        using var connection = _db.Open();
        using var command = new NpgsqlCommand("SELECT business_place_name FROM bc_business_place WHERE is_default", connection);
        return (string)command.ExecuteScalar()!;
    }

    private string TimeZone()
    {
        using var connection = _db.Open();
        using var command = new NpgsqlCommand("SELECT time_zone FROM bc_company", connection);
        return (string)command.ExecuteScalar()!;
    }

    private List<(string, int, string)> Decimals()
    {
        using var connection = _db.Open();
        using var command = new NpgsqlCommand(
            "SELECT module, qty_decimals, rounding_mode FROM sys_decimal_setting ORDER BY module", connection);
        using var reader = command.ExecuteReader();
        var rows = new List<(string, int, string)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
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
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            VALUES ('Quro', 'US', 'USD', 'en', 'America/New_York', 's', 's');
            INSERT INTO bc_exchange_rate (rate_dt, currency_code, rate, created_by, updated_by) VALUES (DATE '2026-10-01', 'KRW', 0.00075, 's', 's');
            """, connection))
        {
            command.ExecuteNonQuery();
        }

        var act = () => CompanySetup.SetCountryAsync(_db.ConnectionString, "KR", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        using var check = _db.Open();
        using var query = new NpgsqlCommand("SELECT country_code FROM bc_company", check);
        query.ExecuteScalar().Should().Be("US");
    }
}
