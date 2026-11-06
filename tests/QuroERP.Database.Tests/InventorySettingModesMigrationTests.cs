using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0415_mm_inventory_setting_modes (TASK-CORE-025): ALLOW becomes ALLOW_IN_MONTH and is no longer accepted (MM-BR-003), and the
/// cost adjustment edit switch starts off (MM-BR-420). Real PostgreSQL; each test works in its own schema.
/// </summary>
public class InventorySettingModesMigrationTests
{
    private const string Script = "0415_mm_inventory_setting_modes.sql";

    [DatabaseTheory, Trait("Id", "MM-SET-IT-20"), Trait("Rule", "MM-BR-003")]
    [InlineData("ALLOW", "ALLOW_IN_MONTH")]
    [InlineData("ALLOW_IN_MONTH", "ALLOW_IN_MONTH")]
    [InlineData("DENY", "DENY")]
    public void ExistingRow_KeepsItsMeaning_AndTheEditSwitchStartsOff(string before, string after)
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute($"UPDATE mm_inventory_setting SET negative_stock_mode = '{before}'");

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', negative_stock_mode, over_receipt_mode, allow_cost_adjustment_edit) FROM mm_inventory_setting")
            .Should().Equal($"{after} DENY f");
    }

    [DatabaseFact, Trait("Id", "MM-SET-IT-21"), Trait("Rule", "MM-BR-003")]
    public void Allow_IsRejected()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);

        var act = () => schema.Execute("UPDATE mm_inventory_setting SET negative_stock_mode = 'ALLOW'");

        act.Should().Throw<PostgresException>().Which.ConstraintName.Should().Be("ck_mm_inventory_setting_negative_stock_mode");
    }
}
