using FluentAssertions;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0416_price_level_upper_case (TASK-BC-028): customer and sales price levels become trimmed upper case (SD-BR-304, SD-BR-409).
/// Real PostgreSQL; each test works in its own schema.
/// </summary>
public class PriceLevelUpperCaseMigrationTests
{
    private const string Script = "0416_price_level_upper_case.sql";

    [DatabaseFact, Trait("Id", "BC-028-IT-01"), Trait("Rule", "SD-BR-409")]
    public void SalesPriceLevels_BecomeUpperCase_AndACollisionKeepsOneRow()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_item (id, item_cd, created_by, updated_by) VALUES (1, 'A', 't', 't'), (2, 'B', 't', 't'), (3, 'C', 't', 't');
            INSERT INTO bc_sales_price (price_level, item_id, sales_prc, created_by, updated_by, updated_at) VALUES
                ('dlr ', 1, 1, 't', 't', '2026-11-02'),
                ('DLR', 1, 2, 't', 't', '2026-11-01'),
                ('rtl', 2, 3, 't', 't', '2026-11-01'),
                (' Rtl', 2, 4, 't', 't', '2026-11-02'),
                ('Blue River Market', 3, 5, 't', 't', '2026-11-01');
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', price_level, item_id, sales_prc::int) FROM bc_sales_price ORDER BY item_id")
            .Should().Equal("DLR 1 2", "RTL 2 4", "BLUE RIVER MARKET 3 5");
    }

    [DatabaseFact, Trait("Id", "BC-028-IT-02"), Trait("Rule", "SD-BR-304")]
    public void CustomerPriceLevels_BecomeUpperCase()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_customer (c_name, price_level, created_by, updated_by) VALUES
                ('ACME', ' Whole Sale ', 't', 't'), ('BETA', 'BETA', 't', 't'), ('GAMMA', NULL, 't', 't');
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', c_name, coalesce(price_level, '-')) FROM bc_customer ORDER BY c_name")
            .Should().Equal("ACME WHOLE SALE", "BETA BETA", "GAMMA -");
    }
}
