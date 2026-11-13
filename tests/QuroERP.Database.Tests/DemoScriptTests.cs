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
            SELECT count(*) FROM (SELECT o.item_id, sum(o.onhand_qty) AS qty FROM mm_onhand o
                                  JOIN bc_warehouse w ON w.id = o.warehouse_id AND w.is_default GROUP BY o.item_id) o
            JOIN bc_item i ON i.id = o.item_id
            WHERE (i.item_cd, o.qty) IN (('DEMO-BOLT-10', 1000), ('DEMO-PANEL-01', 40), ('DEMO-CABLE-LOT', 50))
              AND i.onhand_qty = o.qty AND i.mvg_cost = i.usd_prc
            """).Should().Be(3);
        Scalar(connection, """
            SELECT count(*) FROM mm_onhand o JOIN mm_lot l ON l.id = o.lot_id
            WHERE (l.lot_no, o.onhand_qty) IN (('L2611A', 30), ('L2611B', 20))
            """).Should().Be(2);
        Scalar(connection, "SELECT count(*) FROM mm_goods_movement WHERE ref_key = 'DEMO-OPENING' AND mov_type = 'GR'").Should().Be(4);
    }

    [DatabaseFact, Trait("Id", "DB-IT-11")]
    public void UpgradeWithDemo_AddsAnOpenAndAPartlyPaidInvoice_WithTheCountersMovedOn()
    {
        var result = DatabaseMigrator.Upgrade(_connectionString, includeDemo: true, _schema, new NoOpUpgradeLog());

        result.Successful.Should().BeTrue(result.Error?.Message);
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        Scalar(connection, """
            SELECT count(*) FROM sd_sales_invoice d JOIN bc_customer c ON c.id = d.customer_id AND c.c_name = 'MAPLE HILL SUPPLY'
            WHERE (d.status, d.tot_dn_amt, d.bal_amt) IN (('OP', 95, 95), ('PA', 190, 90))
              AND (SELECT sum(l.dn_amt) FROM sd_sales_invoice_line l WHERE l.sales_invoice_id = d.id) = d.tot_dn_amt
            """).Should().Be(2);
        Scalar(connection, """
            SELECT count(*) FROM sd_ar_clear a JOIN sd_sales_invoice d ON d.id = a.sales_invoice_id
            WHERE d.status = 'PA' AND a.cls_amt = d.tot_dn_amt - d.bal_amt
            """).Should().Be(1, "the paid part is what the demo receipt applied");
        Scalar(connection, """
            SELECT count(*) FROM sys_document_number n
            WHERE (n.trans_type = 'DN' AND n.trans_no = (SELECT max(dn_no) FROM sd_sales_invoice))
               OR (n.trans_type = 'CR' AND n.period = '2611' AND n.trans_no = (SELECT max(cls_ar_no) FROM sd_ar_receipt))
            """).Should().Be(2, "the next invoice and receipt numbers come after the demo ones");
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
