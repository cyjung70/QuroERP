using FluentAssertions;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0419_bc_item_set_to_kit (TASK-CORE-069): every existing Set item becomes a Kit, because it expands into its components and
/// keeps no stock (SD-BR-511). Real PostgreSQL; each test works in its own schema.
/// </summary>
public class KitItemTypeMigrationTests
{
    private const string Script = "0419_bc_item_set_to_kit.sql";

    [DatabaseFact, Trait("Id", "SD-UT-511"), Trait("Rule", "SD-BR-511")]
    public void SetItems_BecomeKits_AndOtherTypesStay()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_item (id, item_cd, type, created_by, updated_by) VALUES
                (1, 'GIFT', 'SET', 't', 't'), (2, 'BOLT', 'INVENTORY', 't', 't'), (3, 'FREIGHT', 'SERVICE', 't', 't'), (4, 'NONE', NULL, 't', 't');
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("SELECT concat_ws(' ', item_cd, coalesce(type, '-')) FROM bc_item ORDER BY id")
            .Should().Equal("GIFT KIT", "BOLT INVENTORY", "FREIGHT SERVICE", "NONE -");
    }
}
