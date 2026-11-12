using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0408_mm_supplier_invoice (TASK-MM-047): the supplier invoice tables, one vendor invoice number per vendor among invoices that
/// are not void, and lines that go with their invoice. Real PostgreSQL; each test works in its own schema.
/// </summary>
public class SupplierInvoiceMigrationTests
{
    private const string Script = "0408_mm_supplier_invoice.sql";

    private const string Vendor = """
        INSERT INTO bc_vendor (v_name, currency_code, created_by, updated_by) VALUES ('Vendor', 'USD', 't', 't');
        """;

    [DatabaseFact, Trait("Id", "MM-UT-764"), Trait("Rule", "MM-BR-764")]
    public void ARefNo_IsUniquePerVendor_ExceptOnVoidInvoices()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);
        schema.Execute(Vendor + Insert("VI1", "VO") + Insert("VI2", "OP"));

        var duplicate = () => schema.Execute(Insert("VI3", "OP"));

        duplicate.Should().Throw<PostgresException>().Which.ConstraintName.Should().Be("uq_mm_supplier_invoice_vendor_id_ref_no");
        schema.Rows("SELECT concat_ws(' ', invoice_no, type, status, cls_amt, tax_invoice_received) FROM mm_supplier_invoice ORDER BY invoice_no")
            .Should().Equal("VI1 INV VO 0 f", "VI2 INV OP 0 f");
    }

    [DatabaseFact, Trait("Id", "MM-UT-760"), Trait("Rule", "MM-BR-760")]
    public void Lines_GoWithTheirInvoice()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);
        schema.Execute(Vendor + Insert("VI1", "OP") + """
            INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by) VALUES ('BOLT', 'Bolt', 'INVENTORY', 't', 't');
            INSERT INTO mm_supplier_invoice_line (supplier_invoice_id, seq, item_id, qty, prc, tax_code_id, created_by, updated_by)
            SELECT s.id, 1, (SELECT id FROM bc_item WHERE item_cd = 'BOLT'), 1, 10, (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), 't', 't'
            FROM mm_supplier_invoice s;
            DELETE FROM mm_supplier_invoice;
            """);

        schema.Rows("SELECT count(*)::text FROM mm_supplier_invoice_line").Should().Equal("0");
    }

    private static string Insert(string invoiceNo, string status) => $"""
        INSERT INTO mm_supplier_invoice (invoice_no, vendor_id, business_place_id, ref_no, invoice_dt, due_dt, currency_code, exchange_rate,
                                         status, created_by, updated_by)
        SELECT '{invoiceNo}', (SELECT id FROM bc_vendor WHERE v_name = 'Vendor'), id, 'INV-1', '2026-11-20', '2026-11-20', 'USD', 1,
               '{status}', 't', 't'
        FROM bc_business_place WHERE is_default;
        """;
}
