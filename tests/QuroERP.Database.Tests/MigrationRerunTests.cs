using DbUp.Engine.Output;
using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// A migration that a database may already hold under another script name runs again over its own objects (real PostgreSQL).
/// The dev database got the sales invoice cancellation objects from an earlier 0413 script of the same content.
/// </summary>
public sealed class MigrationRerunTests : IDisposable
{
    private const string CancellationScript = "QuroERP.Database.Scripts.Schema.0417_sd_sales_invoice_cancellation.sql";

    private readonly string _schema = $"t_{Guid.NewGuid():N}";
    private readonly string _connectionString = "";

    public MigrationRerunTests()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            return;
        }
        _connectionString = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { SearchPath = _schema }.ToString();
        Execute(TestDatabase.ConnectionString, $"CREATE SCHEMA {_schema}");
    }

    [DatabaseFact, Trait("Id", "DB-IT-01")]
    public void SalesInvoiceCancellation_OverItsOwnObjects_RunsAgain_AndKeepsOneOfEach()
    {
        DatabaseMigrator.Upgrade(_connectionString, includeDemo: false, _schema, new NoOpUpgradeLog()).Successful.Should().BeTrue();
        // The objects stay, the journal forgets the script: the state of a database that ran the same script under 0413
        Execute(_connectionString, $"DELETE FROM {DatabaseMigrator.JournalTable} WHERE scriptname = '{CancellationScript}'");

        var rerun = DatabaseMigrator.Upgrade(_connectionString, includeDemo: false, _schema, new NoOpUpgradeLog());

        rerun.Successful.Should().BeTrue(rerun.Error?.Message);
        rerun.Scripts.Select(s => s.Name).Should().Equal(CancellationScript);
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        Scalar(connection, $"""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = '{_schema}' AND table_name = 'sd_sales_invoice_line' AND column_name IN ('lot_id', 'cancelled_invoice_line_id')
            """).Should().Be(2);
        Scalar(connection, """
            SELECT count(*) FROM pg_constraint
            WHERE conrelid = 'sd_sales_invoice_line'::regclass
              AND conname IN ('fk_sd_sales_invoice_line_lot_id', 'fk_sd_sales_invoice_line_cancelled_invoice_line_id')
            """).Should().Be(2);
        Scalar(connection, $"""
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = '{_schema}' AND indexname IN ('ix_sd_sales_invoice_line_lot_id',
                'ix_sd_sales_invoice_line_cancelled_invoice_line_id', 'ix_sd_sales_invoice_type_dn_dt')
            """).Should().Be(3);
    }

    public void Dispose()
    {
        if (_connectionString.Length > 0)
        {
            Execute(TestDatabase.ConnectionString!, $"DROP SCHEMA {_schema} CASCADE");
        }
    }

    private static long Scalar(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    // The schema is the t_<guid> this class made and the script name a constant (not outside input)
    private static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
