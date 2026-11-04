using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0407_mm_goods_receipt_stock_only (TASK-MM-007): the receipt loses its payable columns, lines get the lot and the quantities
/// that lock them, PO lines get their received qty and the inventory setting the over-receipt mode. Real PostgreSQL; each test
/// works in its own schema.
/// </summary>
public class GoodsReceiptStockOnlyMigrationTests
{
    private const string Script = "0407_mm_goods_receipt_stock_only.sql";

    [DatabaseFact, Trait("Id", "MM-UT-108"), Trait("Rule", "MM-BR-108")]
    public void ExistingReceipts_FillThePoLineReceivedQty_AndLinesStartUnmatched()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO mm_purchase_order (po_no, currency_code, exchange_rate, business_place_id, created_by, updated_by)
            SELECT 'PO2610000001', 'USD', 1, id, 't', 't' FROM bc_business_place WHERE is_default;
            INSERT INTO mm_purchase_order_line (purchase_order_id, seq, po_qty, tax_code_id, created_by, updated_by)
            SELECT p.id, s, 10, t.id, 't', 't'
            FROM mm_purchase_order p CROSS JOIN generate_series(1, 2) s CROSS JOIN bc_tax_code t WHERE t.tax_code = 'NONE';
            INSERT INTO mm_goods_receipt (gr_no, warehouse_id, currency_code, exchange_rate, business_place_id, bal_amt, terms, created_by, updated_by)
            SELECT g, (SELECT min(id) FROM bc_warehouse), 'USD', 1, (SELECT id FROM bc_business_place WHERE is_default), 5, 'NET30', 't', 't'
            FROM unnest(ARRAY['GR1', 'GR2']) g;
            INSERT INTO mm_goods_receipt_line (goods_receipt_id, seq, gr_qty, purchase_order_line_id, warehouse_id, tax_code_id, created_by, updated_by)
            SELECT g.id, 1, q, (SELECT id FROM mm_purchase_order_line WHERE seq = 1), (SELECT min(id) FROM bc_warehouse),
                   (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), 't', 't'
            FROM mm_goods_receipt g JOIN (VALUES ('GR1', 3), ('GR2', 4)) v (gr_no, q) ON v.gr_no = g.gr_no;
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', seq, receipt_qty) FROM mm_purchase_order_line ORDER BY seq").Should().Equal("1 7", "2 0");
        schema.Rows("SELECT concat_ws(' ', lot_id IS NULL, invoiced_qty, returned_qty) FROM mm_goods_receipt_line")
            .Should().Equal("t 0 0", "t 0 0");
        schema.Rows("""
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'mm_goods_receipt'
              AND column_name IN ('cls_amt', 'bal_amt', 'due_dt', 'terms', 'tax_invoice_received')
            """).Should().BeEmpty();
        schema.Rows("SELECT over_receipt_mode FROM mm_inventory_setting").Should().Equal("DENY");
    }

    [DatabaseFact, Trait("Id", "MM-UT-102"), Trait("Rule", "MM-BR-102")]
    public void AnUnknownOverReceiptMode_IsRejected()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);

        var act = () => schema.Execute("UPDATE mm_inventory_setting SET over_receipt_mode = 'ALLOW'");

        act.Should().Throw<PostgresException>().Which.ConstraintName.Should().Be("ck_mm_inventory_setting_over_receipt_mode");
    }
}
