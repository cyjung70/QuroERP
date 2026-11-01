using FluentAssertions;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0403_bc_exchange_rate_by_currency (TASK-BC-047): legacy rows per date become rows per date and currency (SD-BR-906, SD-BR-909).
/// Real PostgreSQL; each test works in its own schema.
/// </summary>
public class ExchangeRateMigrationTests
{
    private const string Script = "0403_bc_exchange_rate_by_currency.sql";

    [DatabaseFact, Trait("Id", "BC-UC-024-T07"), Trait("Id", "SD-UT-909"), Trait("Rule", "SD-BR-909")]
    public void TwentyOneLegacyDays_BecomeFortyTwoRows_WithTheInverseRates()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_exchange_rate (cur_dt, cur_rp, cur_won, created_by, updated_by)
            SELECT DATE '2026-10-01' + n, 15900 + n, 1350, 'mig', 'mig' FROM generate_series(0, 20) AS n;
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT count(*)::text FROM bc_exchange_rate").Should().Equal("42");
        schema.Rows("""
            SELECT concat_ws(' ', rate_dt, currency_code, rate, created_by)
            FROM bc_exchange_rate WHERE rate_dt = DATE '2026-10-01' ORDER BY currency_code
            """).Should().Equal("2026-10-01 IDR 0.0000628931 mig", "2026-10-01 KRW 0.0007407407 mig");
    }

    [DatabaseFact, Trait("Id", "SD-UT-909"), Trait("Rule", "SD-BR-909")]
    public void AMissingOrZeroLegacyValue_MakesNoRow()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_exchange_rate (cur_dt, cur_rp, cur_won, created_by, updated_by)
            VALUES (DATE '2026-10-01', NULL, 1350, 'mig', 'mig'), (DATE '2026-10-02', 0, NULL, 'mig', 'mig');
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', rate_dt, currency_code) FROM bc_exchange_rate").Should().Equal("2026-10-01 KRW");
    }

    [DatabaseFact, Trait("Id", "SD-UT-906"), Trait("Rule", "SD-BR-906")]
    public void OneRatePerDateAndCurrency_AndTheColumnsAreRequired()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);
        schema.Execute("""
            INSERT INTO bc_exchange_rate (rate_dt, currency_code, rate, created_by, updated_by)
            VALUES (DATE '2026-10-01', 'KRW', 0.00075, 't', 't'), (DATE '2026-10-01', 'IDR', 0.00006, 't', 't');
            """);

        var twice = () => schema.Execute("""
            INSERT INTO bc_exchange_rate (rate_dt, currency_code, rate, created_by, updated_by) VALUES (DATE '2026-10-01', 'KRW', 1, 't', 't')
            """);
        var noRate = () => schema.Execute("""
            INSERT INTO bc_exchange_rate (rate_dt, currency_code, created_by, updated_by) VALUES (DATE '2026-10-02', 'KRW', 't', 't')
            """);

        twice.Should().Throw<Npgsql.PostgresException>().Which.ConstraintName.Should().Be("uq_bc_exchange_rate_rate_dt_currency_code");
        noRate.Should().Throw<Npgsql.PostgresException>().Which.SqlState.Should().Be("23502");
        schema.Rows("SELECT count(*)::text FROM bc_exchange_rate").Should().Equal("2");
    }
}
