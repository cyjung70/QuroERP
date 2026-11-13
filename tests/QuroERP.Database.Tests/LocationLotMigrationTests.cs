using DbUp;
using DbUp.Engine.Output;
using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// The Location and Lot stock key migration (0418) keeps every item + warehouse balance and ledger total, and runs again over its
/// own objects (real PostgreSQL, TASK-CORE-067).
/// </summary>
public sealed class LocationLotMigrationTests : IDisposable
{
    private const string SchemaPrefix = "QuroERP.Database.Scripts.Schema.";
    private const string StockKeyScript = SchemaPrefix + "0418_mm_location_lot_stock_key.sql";

    private readonly string _schema = $"t_{Guid.NewGuid():N}";
    private readonly string _connectionString = "";

    public LocationLotMigrationTests()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
        {
            return;
        }
        _connectionString = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { SearchPath = _schema }.ToString();
        Execute(TestDatabase.ConnectionString, $"CREATE SCHEMA {_schema}");
    }

    [DatabaseFact, Trait("Id", "DB-IT-01"), Trait("Rule", "MM-BR-714")]
    public void StockFromBefore_MovesToLocationAndLotKeys_WithTheSameItemWarehouseAndLedgerTotals()
    {
        UpgradeBeforeTheStockKey();
        Execute(_connectionString, """
            INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, onhand_qty, created_by, updated_by)
            VALUES ('T-PLAIN', 'Plain', 'INVENTORY', 'EA', 'Y', 10, 't', 't'),
                   ('T-LOT', 'Lot', 'INVENTORY', 'EA', 'Y', 8, 't', 't');
            UPDATE bc_item SET lot_managed = true, lot_managed_from = DATE '2026-10-01' WHERE item_cd = 'T-LOT';
            INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
            SELECT id, v.lot_no, 't', 't' FROM bc_item CROSS JOIN (VALUES ('L1'), ('MIGRATED')) v (lot_no) WHERE item_cd = 'T-LOT';
            INSERT INTO mm_onhand (item_id, warehouse_id, onhand_qty, created_by, updated_by)
            SELECT i.id, w.id, i.onhand_qty, 't', 't' FROM bc_item i JOIN bc_warehouse w ON w.is_default WHERE i.item_cd LIKE 'T-%';
            INSERT INTO mm_lot_onhand (item_id, warehouse_id, lot_id, onhand_qty, created_by, updated_by)
            SELECT l.item_id, w.id, l.id, CASE l.lot_no WHEN 'L1' THEN 5 ELSE 3 END, 't', 't'
            FROM mm_lot l JOIN bc_warehouse w ON w.is_default;
            INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, lot_id, qty, prc, amount, ref_key,
                                           ref_seq, created_by, updated_by)
            SELECT v.no, DATE '2026-10-02', 'GR', i.id, w.id, l.id, v.qty, 1, v.qty, 'T', 1, 't', 't'
            FROM (VALUES ('ID-T-1', 'T-PLAIN', NULL, 10), ('ID-T-2', 'T-LOT', 'L1', 5), ('ID-T-3', 'T-LOT', 'MIGRATED', 3)) v (no, item_cd, lot_no, qty)
            JOIN bc_item i ON i.item_cd = v.item_cd JOIN bc_warehouse w ON w.is_default
            LEFT JOIN mm_lot l ON l.item_id = i.id AND l.lot_no = v.lot_no;
            """);
        var before = Totals();

        var result = DatabaseMigrator.Upgrade(_connectionString, includeDemo: false, _schema, new NoOpUpgradeLog());

        result.Successful.Should().BeTrue(result.Error?.Message);
        Totals().Should().Equal(before);
        using var connection = Open();
        Rows(connection, """
            SELECT i.item_cd || ' ' || c.location_code || ' ' || l.lot_no || ' ' || o.onhand_qty::int
            FROM mm_onhand o JOIN bc_item i ON i.id = o.item_id JOIN bc_location c ON c.id = o.location_id JOIN mm_lot l ON l.id = o.lot_id
            WHERE i.item_cd LIKE 'T-%' ORDER BY 1
            """).Should().Equal("T-LOT * * 3", "T-LOT * L1 5", "T-PLAIN * * 10");
        Rows(connection, """
            SELECT g.document_no || ' ' || c.location_code || ' ' || l.lot_no
            FROM mm_goods_movement g JOIN bc_location c ON c.id = g.location_id JOIN mm_lot l ON l.id = g.lot_id ORDER BY 1
            """).Should().Equal("ID-T-1 * *", "ID-T-2 * L1", "ID-T-3 * *");
        Scalar(connection, "SELECT count(*) FROM mm_lot WHERE lot_no IN ('MIGRATED', 'UNKNOWN')").Should().Be(0);
        Scalar(connection, "SELECT count(*) FROM mm_lot l JOIN bc_item i ON i.id = l.item_id WHERE i.item_cd = 'T-LOT' AND l.lot_no = 'L1' AND l.received_dt = DATE '2026-10-02'")
            .Should().Be(1, "an existing lot is received on its first incoming movement (FIFO order)");
        Scalar(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'mm_lot_onhand'")
            .Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "DB-IT-01")]
    public void StockKey_OverItsOwnObjects_RunsAgain_AndKeepsOneSystemRowEach()
    {
        DatabaseMigrator.Upgrade(_connectionString, includeDemo: false, _schema, new NoOpUpgradeLog()).Successful.Should().BeTrue();
        Execute(_connectionString, $"DELETE FROM {DatabaseMigrator.JournalTable} WHERE scriptname = '{StockKeyScript}'");

        var rerun = DatabaseMigrator.Upgrade(_connectionString, includeDemo: false, _schema, new NoOpUpgradeLog());

        rerun.Successful.Should().BeTrue(rerun.Error?.Message);
        rerun.Scripts.Select(s => s.Name).Should().Equal(StockKeyScript);
        using var connection = Open();
        Scalar(connection, "SELECT count(*) FROM bc_warehouse w WHERE (SELECT count(*) FROM bc_location c WHERE c.warehouse_id = w.id AND c.location_code = '*') <> 1")
            .Should().Be(0);
        Scalar(connection, "SELECT count(*) FROM bc_item i WHERE (SELECT count(*) FROM mm_lot l WHERE l.item_id = i.id AND l.lot_no = '*') <> 1")
            .Should().Be(0);
        Scalar(connection, """
            SELECT count(*) FROM pg_constraint
            WHERE conname IN ('uq_mm_onhand_item_id_warehouse_id_location_id_lot_id', 'fk_mm_goods_movement_location_id',
                              'fk_mm_stock_transfer_line_from_location_id', 'ck_sd_sales_invoice_line_lot_id', 'fk_mm_lot_goods_receipt_line_id')
              AND connamespace = current_schema()::regnamespace
            """).Should().Be(5);
    }

    public void Dispose()
    {
        if (_connectionString.Length > 0)
        {
            Execute(TestDatabase.ConnectionString!, $"DROP SCHEMA {_schema} CASCADE");
        }
    }

    // The schema scripts before 0418 only, so the test holds stock in the earlier shape
    private void UpgradeBeforeTheStockKey()
    {
        var result = DeployChanges.To
            .PostgresqlDatabase(_connectionString)
            .JournalToPostgresqlTable(_schema, DatabaseMigrator.JournalTable)
            .WithTransactionPerScript()
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly,
                name => name.StartsWith(SchemaPrefix, StringComparison.Ordinal) && string.CompareOrdinal(name, StockKeyScript) < 0)
            .LogTo(new NoOpUpgradeLog())
            .Build()
            .PerformUpgrade();
        result.Successful.Should().BeTrue(result.Error?.Message);
    }

    // Item + warehouse on hand, ledger and item totals of the test items
    private List<string> Totals()
    {
        using var connection = Open();
        return Rows(connection, """
            SELECT i.item_cd || ' ' || o.qty::int || ' ' || g.qty::int || ' ' || i.onhand_qty::int
            FROM bc_item i
            JOIN (SELECT item_id, warehouse_id, sum(onhand_qty) AS qty FROM mm_onhand GROUP BY item_id, warehouse_id) o ON o.item_id = i.id
            JOIN (SELECT item_id, warehouse_id, sum(qty) AS qty FROM mm_goods_movement GROUP BY item_id, warehouse_id) g
              ON g.item_id = o.item_id AND g.warehouse_id = o.warehouse_id
            WHERE i.item_cd LIKE 'T-%' ORDER BY 1
            """);
    }

    private NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static List<string> Rows(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }
        return rows;
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
