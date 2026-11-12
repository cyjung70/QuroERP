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
        Scalar(connection, "SELECT count(*) FROM bc_item WHERE item_cd LIKE 'DEMO-%' AND NOT lot_managed AND usd_prc > 0").Should().Be(3);
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

    [DatabaseFact, Trait("Id", "DB-IT-11")]
    public void UpgradeWithDemo_PostsTheShipmentStock_InTheDefaultWarehouse_WithTheLotBalances()
    {
        var result = DatabaseMigrator.Upgrade(_connectionString, includeDemo: true, _schema, new NoOpUpgradeLog());

        result.Successful.Should().BeTrue(result.Error?.Message);
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        Scalar(connection, """
            SELECT count(*) FROM mm_onhand o JOIN bc_item i ON i.id = o.item_id JOIN bc_warehouse w ON w.id = o.warehouse_id AND w.is_default
            WHERE (i.item_cd, o.onhand_qty) IN (('DEMO-BOLT-10', 1100), ('DEMO-PANEL-01', 42), ('DEMO-CABLE-LOT', 50))
              AND i.onhand_qty = o.onhand_qty AND i.mvg_cost = i.usd_prc
            """).Should().Be(3, "the opening stock and the supplier invoice demo receipt (0909) at the same prices");
        Scalar(connection, """
            SELECT count(*) FROM mm_lot_onhand o JOIN mm_lot l ON l.id = o.lot_id
            WHERE (l.lot_no, o.onhand_qty) IN (('L2611A', 30), ('L2611B', 20))
            """).Should().Be(2);
        Scalar(connection, "SELECT count(*) FROM mm_goods_movement WHERE ref_key = 'DEMO-OPENING' AND mov_type = 'GR'").Should().Be(4);
    }

    [DatabaseFact, Trait("Id", "DB-IT-11"), Trait("Rule", "MM-BR-763")]
    public void UpgradeWithDemo_AddsAnOpenSupplierInvoice_AndAReceiptLineNotInvoiced()
    {
        var result = DatabaseMigrator.Upgrade(_connectionString, includeDemo: true, _schema, new NoOpUpgradeLog());

        result.Successful.Should().BeTrue(result.Error?.Message);
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        Scalar(connection, """
            SELECT count(*) FROM mm_supplier_invoice s JOIN bc_vendor v ON v.id = s.vendor_id
            JOIN sys_document_number n ON n.trans_type = 'VI' AND n.period = '2611' AND n.trans_no = s.invoice_no
            WHERE v.v_name = 'MAPLE LEAF SUPPLY' AND s.invoice_no = 'VI2611000001' AND s.type = 'INV' AND s.status = 'OP'
              AND s.tot_amt = 45 AND s.bal_amt = 45 AND s.cls_amt = 0
            """).Should().Be(1, "the API numbers the next invoice after the demo one");
        Scalar(connection, """
            SELECT count(*) FROM mm_goods_receipt g JOIN mm_goods_receipt_line l ON l.goods_receipt_id = g.id
            JOIN sys_document_number n ON n.trans_type = 'GR' AND n.period = '2611' AND n.trans_no = g.gr_no
            LEFT JOIN mm_supplier_invoice_line s ON s.goods_receipt_line_id = l.id
            WHERE g.ref_no = 'DEMO-PL-2611'
              AND ((l.seq = 1 AND l.invoiced_qty = l.gr_qty AND s.qty = l.gr_qty) OR (l.seq = 2 AND l.invoiced_qty = 0 AND s.id IS NULL))
            """).Should().Be(2, "line 1 is invoiced in full, line 2 is left for Import Receipts");
        Scalar(connection, "SELECT count(*) FROM mm_goods_movement m JOIN mm_goods_receipt g ON g.gr_no = m.ref_key WHERE g.ref_no = 'DEMO-PL-2611'")
            .Should().Be(2);
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
