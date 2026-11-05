using DbUp.Engine.Output;
using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>The demo scripts (--demo) apply on top of the migrations in a schema of their own (real PostgreSQL).</summary>
public sealed class DemoScriptTests : IDisposable
{
    private readonly string _schema = $"t_{Guid.NewGuid():N}";
    private readonly string _connectionString = "";

    public DemoScriptTests()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            return;
        }
        _connectionString = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { SearchPath = _schema }.ToString();
        Execute(TestDatabase.ConnectionString, $"CREATE SCHEMA {_schema}");
    }

    [DatabaseFact, Trait("Id", "DB-IT-11")]
    public void UpgradeWithDemo_AppliesTheDemoScripts_WithThePurchaseOrderItemsVendorPriceAndLotItem()
    {
        var result = DatabaseMigrator.Upgrade(_connectionString, includeDemo: true, _schema, new NoOpUpgradeLog());

        result.Successful.Should().BeTrue(result.Error?.Message);
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        Scalar(connection, "SELECT count(*) FROM bc_item WHERE item_cd LIKE 'DEMO-%' AND usd_prc > 0").Should().Be(4);
        Scalar(connection, "SELECT count(*) FROM bc_item WHERE item_cd LIKE 'DEMO-%' AND type = 'SERVICE'").Should().Be(1);
        Scalar(connection, """
            SELECT count(*) FROM bc_purchase_price p JOIN bc_vendor v ON v.vendor_price = p.vendor_price
            JOIN bc_item i ON i.id = p.item_id
            WHERE v.v_name = 'MAPLE LEAF SUPPLY' AND i.item_cd = 'DEMO-BOLT-10' AND p.purchase_prc <> i.usd_prc
            """).Should().Be(1);
        Scalar(connection, """
            SELECT count(*) FROM bc_item
            WHERE item_cd = 'DEMO-RESIN-LOT' AND type = 'INVENTORY' AND lot_managed AND lot_managed_from IS NOT NULL
            """).Should().Be(1, "the goods receipt screen shows the lot column for it");
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

    // The schema is the t_<guid> this class made (not outside input)
    private static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
