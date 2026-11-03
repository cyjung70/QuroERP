using FluentAssertions;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0401_bc_business_place (TASK-CORE-046): the default business place copied from the company, and business_place_id on the
/// warehouse and the document headers (BC-BR-315, BC-BR-319, BC-BR-321). Real PostgreSQL; each test works in its own schema.
/// </summary>
public class BusinessPlaceMigrationTests
{
    private const string Script = "0401_bc_business_place.sql";

    private static readonly string[] KeyedTables =
        ["bc_warehouse", "sd_sales_order", "sd_sales_invoice", "sd_shipment", "sd_ar_receipt", "mm_purchase_order", "mm_goods_receipt", "mm_ap_payment"];

    [DatabaseFact, Trait("Id", "CORE-046-IT-01"), Trait("Rule", "BC-BR-315")]
    public void DefaultPlace_IsCopiedFromTheCompany()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, register_no, represent, tel_no,
                                    business_type, business_item, created_by, updated_by)
            VALUES ('Quro', 'KR', 'KRW', 'ko', 'Asia/Seoul', '220-81-62517', 'Kim', '02-000-0000', 'Service', 'Software', 't', 't');
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("""
            SELECT concat_ws(' ', business_place_code, business_place_name, is_default, business_reg_no, ceo_name, tel_no, business_type,
                             business_item, use_yn)
            FROM bc_business_place
            """).Should().Equal("MAIN Quro t 2208162517 Kim 02-000-0000 Service Software t");
    }

    [DatabaseFact, Trait("Id", "CORE-046-IT-02"), Trait("Rule", "BC-BR-319")]
    public void WithoutACompany_TheDefaultPlaceIsNamedByItsCode()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', business_place_code, business_place_name, is_default) FROM bc_business_place")
            .Should().Equal("MAIN MAIN t");
    }

    [DatabaseFact, Trait("Id", "CORE-046-IT-03"), Trait("Rule", "BC-BR-321")]
    public void ExistingWarehousesAndDocuments_GetTheDefaultPlace_AndTheColumnIsRequired()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            VALUES ('Quro', 'KR', 'KRW', 'ko', 'Asia/Seoul', 't', 't');
            INSERT INTO bc_warehouse (warehouse_code, warehouse_name, created_by, updated_by) VALUES ('W02', 'Second', 't', 't');
            INSERT INTO mm_purchase_order (po_no, currency_code, exchange_rate, created_by, updated_by) VALUES ('PO1', 'KRW', 1, 't', 't');
            INSERT INTO sd_ar_receipt (cls_ar_no, cls_dt, pay_method, currency_code, exchange_rate, created_by, updated_by)
            VALUES ('AR1', DATE '2026-10-30', 'CASH', 'KRW', 1, 't', 't');
            """);

        schema.ApplySchemaScript(Script);

        foreach (var table in KeyedTables)
        {
            // The table name comes from the list above, never input
            schema.Rows($"""
                SELECT concat_ws(' ', count(*), count(*) FILTER (WHERE business_place_id = (SELECT id FROM bc_business_place WHERE is_default)))
                FROM {table}
                """).Single().Split(' ').Distinct().Should().ContainSingle($"every {table} row has the default place");
            schema.Rows($"""
                SELECT concat_ws(' ', c.is_nullable, (SELECT count(*) FROM pg_constraint k
                                                      WHERE k.conname = 'fk_{table}_business_place_id' AND k.contype = 'f'
                                                        AND k.connamespace = current_schema()::regnamespace))
                FROM information_schema.columns c
                WHERE c.table_schema = current_schema() AND c.table_name = '{table}' AND c.column_name = 'business_place_id'
                """).Should().Equal(["NO 1"], table);
        }
        schema.Rows("SELECT count(*)::text FROM bc_warehouse").Should().Equal("2");
        schema.Rows("SELECT count(*)::text FROM mm_purchase_order").Should().Equal("1");
    }

    [DatabaseFact, Trait("Id", "CORE-046-IT-04"), Trait("Rule", "BC-BR-319")]
    public void ASecondDefaultPlace_IsRefused()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);

        var act = () => schema.Execute("""
            INSERT INTO bc_business_place (business_place_code, business_place_name, is_default, created_by, updated_by)
            VALUES ('SECOND', 'Second', true, 't', 't')
            """);

        act.Should().Throw<Npgsql.PostgresException>().Which.ConstraintName.Should().Be("uq_bc_business_place_single_default");
    }
}
