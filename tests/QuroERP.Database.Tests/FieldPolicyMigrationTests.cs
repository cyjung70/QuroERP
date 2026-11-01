using FluentAssertions;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0402_bc_field_policy (TASK-CORE-047): the company override table and ext_attrs on the partner and item masters and the document
/// headers (BC-BR-431, BC-BR-433, BC-BR-436). Real PostgreSQL; each test works in its own schema.
/// </summary>
public class FieldPolicyMigrationTests
{
    private const string Script = "0402_bc_field_policy.sql";

    private static readonly string[] ExtTables =
    [
        "bc_customer", "bc_vendor", "bc_item", "sd_sales_order", "sd_sales_invoice", "sd_shipment", "sd_ar_receipt", "mm_purchase_order",
        "mm_goods_receipt", "mm_ap_payment",
    ];

    [DatabaseFact, Trait("Id", "CORE-047-IT-03"), Trait("Rule", "BC-BR-436")]
    public void ExistingRows_GetAnEmptyExtAttrs_AndTheColumnIsRequired()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO mm_purchase_order (po_no, currency_code, exchange_rate, business_place_id, created_by, updated_by)
            VALUES ('PO1', 'KRW', 1, (SELECT id FROM bc_business_place WHERE is_default), 't', 't');
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT ext_attrs::text FROM mm_purchase_order").Should().Equal("{}");
        foreach (var table in ExtTables)
        {
            // The table name comes from the list above, never input
            schema.Rows($"""
                SELECT concat_ws(' ', data_type, is_nullable, column_default)
                FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = '{table}' AND column_name = 'ext_attrs'
                """).Should().Equal(["jsonb NO '{}'::jsonb"], table);
        }
    }

    [DatabaseFact, Trait("Id", "CORE-047-IT-04"), Trait("Rule", "BC-BR-433")]
    public void FieldPolicy_TakesOneRowPerKey_AndOnlyTheFourStates()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);
        schema.Execute("""
            INSERT INTO bc_field_policy (field_key, state, created_by, updated_by) VALUES ('Sd.Customer.State', 'OPTIONAL', 't', 't');
            INSERT INTO bc_field_policy (field_key, default_value, created_by, updated_by) VALUES ('Sd.Customer.Country', 'US', 't', 't');
            """);

        var twice = () => schema.Execute("INSERT INTO bc_field_policy (field_key, state, created_by, updated_by) VALUES ('Sd.Customer.State', 'HIDDEN', 't', 't')");
        var badState = () => schema.Execute("INSERT INTO bc_field_policy (field_key, state, created_by, updated_by) VALUES ('Sd.Customer.City', 'SHOWN', 't', 't')");

        twice.Should().Throw<Npgsql.PostgresException>().Which.ConstraintName.Should().Be("uq_bc_field_policy_field_key");
        badState.Should().Throw<Npgsql.PostgresException>().Which.ConstraintName.Should().Be("ck_bc_field_policy_state");
        schema.Rows("SELECT count(*)::text FROM bc_field_policy").Should().Equal("2");
    }
}
