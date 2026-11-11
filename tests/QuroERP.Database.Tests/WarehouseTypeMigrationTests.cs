using FluentAssertions;
using Npgsql;

namespace QuroERP.Database.Tests;

/// <summary>
/// 0406_bc_warehouse_type (TASK-CORE-029): the warehouse type and the one consignment warehouse (MM-BR-709). Real PostgreSQL;
/// each test works in its own schema.
/// </summary>
public class WarehouseTypeMigrationTests
{
    private const string Script = "0406_bc_warehouse_type.sql";

    [DatabaseFact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public void ExistingDatabase_GetsOneConsignmentWarehouse_AndExistingWarehousesAreNormal()
    {
        using var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.Execute("""
            INSERT INTO bc_warehouse (warehouse_code, warehouse_name, business_place_id, created_by, updated_by)
            SELECT 'W02', 'Second', id, 't', 't' FROM bc_business_place WHERE is_default
            """);

        schema.ApplySchemaScript(Script);

        schema.Rows("""
            SELECT concat_ws(' ', w.warehouse_code, w.warehouse_name, w.warehouse_type, w.is_default, w.use_yn, w.business_place_id = p.id)
            FROM bc_warehouse w CROSS JOIN bc_business_place p WHERE p.is_default
            ORDER BY w.warehouse_code
            """).Should().Equal("CONSIGN Consignment CONSIGNMENT f t t", "MAIN Main NORMAL t t t", "W02 Second NORMAL f t t");
    }

    [DatabaseFact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public void ASecondConsignmentWarehouse_IsRejected()
    {
        using var schema = Migrated();

        var act = () => schema.Execute(InsertWarehouse("CONSIGN2", "CONSIGNMENT"));

        act.Should().Throw<PostgresException>().Which.ConstraintName.Should().Be("uq_bc_warehouse_single_consignment");
    }

    [DatabaseFact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public void TheConsignmentWarehouse_CannotBeTheDefault()
    {
        using var schema = Migrated();
        schema.Execute("UPDATE bc_warehouse SET is_default = false WHERE warehouse_code = 'MAIN'");

        var act = () => schema.Execute("UPDATE bc_warehouse SET is_default = true WHERE warehouse_code = 'CONSIGN'");

        act.Should().Throw<PostgresException>().Which.ConstraintName.Should().Be("ck_bc_warehouse_consignment_not_default");
    }

    [DatabaseFact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public void AnUnknownType_IsRejected()
    {
        using var schema = Migrated();

        var act = () => schema.Execute(InsertWarehouse("W03", "TRANSIT"));

        act.Should().Throw<PostgresException>().Which.ConstraintName.Should().Be("ck_bc_warehouse_warehouse_type");
    }

    // The values are test constants, never input
    private static string InsertWarehouse(string code, string type) => $"""
        INSERT INTO bc_warehouse (warehouse_code, warehouse_name, warehouse_type, business_place_id, created_by, updated_by)
        SELECT '{code}', 'Test', '{type}', id, 't', 't' FROM bc_business_place WHERE is_default
        """;

    private static ScratchSchema Migrated()
    {
        var schema = new ScratchSchema();
        schema.ApplySchemaScriptsBefore(Script);
        schema.ApplySchemaScript(Script);
        return schema;
    }
}
