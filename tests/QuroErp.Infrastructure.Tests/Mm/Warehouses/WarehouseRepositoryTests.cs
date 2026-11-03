using Dapper;
using FluentAssertions;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Domain.Mm.Warehouses;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Warehouses;

/// <summary>Warehouse choice list and the consignment warehouse on real PostgreSQL (TASK-CORE-022, TASK-CORE-029). Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class WarehouseRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "CORE-022-IT-01"), Trait("Id", "MM-UT-710"), Trait("Rule", "MM-BR-710")]
    public async Task FindOptions_ReturnsWarehousesInUse_InCodeOrder_WithoutTheConsignmentWarehouse()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await InsertWarehousesAsync(uow);

        var options = await db.Get<IWarehouseRepository>().FindOptionsAsync(includeConsignment: false, includeInactive: false, uow.Transaction, None);

        options.Select(o => o.WarehouseCode).Should().Equal("AA1", "MAIN", "ZZ1");
        options.Should().ContainSingle(o => o.WarehouseCode == "MAIN").Which.WarehouseName.Should().Be("Main");
        options.Should().OnlyContain(o => o.Id > 0 && o.WarehouseType == "NORMAL");
    }

    [DatabaseFact, Trait("Id", "MM-UT-710"), Trait("Rule", "MM-BR-710")]
    public async Task FindOptions_ForReportConditions_IncludesTheConsignmentWarehouse_WithItsType()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await InsertWarehousesAsync(uow);

        var options = await db.Get<IWarehouseRepository>().FindOptionsAsync(includeConsignment: true, includeInactive: false, uow.Transaction, None);

        options.Select(o => (o.WarehouseCode, o.WarehouseType)).Should().Equal(
            ("AA1", "NORMAL"), ("CONSIGN", "CONSIGNMENT"), ("MAIN", "NORMAL"), ("ZZ1", "NORMAL"));
    }

    [DatabaseFact, Trait("Id", "MM-UT-709"), Trait("Rule", "MM-BR-709")]
    public async Task FindConsignment_ReturnsTheOneConsignmentWarehouse()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var id = await uow.Transaction.Connection!.QuerySingleAsync<long>(
            "SELECT id FROM bc_warehouse WHERE warehouse_code = 'CONSIGN'", transaction: uow.Transaction);

        var consignment = await db.Get<IWarehouseRepository>().FindConsignmentAsync(uow.Transaction, None);

        consignment.Should().Be(new ConsignmentWarehouse(id, "CONSIGN"));
    }

    [DatabaseFact, Trait("Id", "MM-UT-711"), Trait("Rule", "MM-BR-711")]
    public async Task SellableOnHand_LeavesOutTheConsignmentWarehouse_AndIsZeroWithoutStock()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var connection = uow.Transaction.Connection!;
        await InsertWarehousesAsync(uow);
        var (stocked, empty) = await connection.QuerySingleAsync<(long, long)>(
            """
            WITH i AS (INSERT INTO bc_item (item_cd, item_name, type, created_by, updated_by)
                       VALUES ('ZZQS1', 'Stocked', 'INVENTORY', 't', 't'), ('ZZQS2', 'Empty', 'INVENTORY', 't', 't')
                       RETURNING id, item_cd)
            SELECT (SELECT id FROM i WHERE item_cd = 'ZZQS1'), (SELECT id FROM i WHERE item_cd = 'ZZQS2')
            """,
            transaction: uow.Transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO mm_onhand (item_id, warehouse_id, onhand_qty, created_by, updated_by)
            SELECT @Item, w.id, v.qty, 't', 't'
            FROM (VALUES ('MAIN', 5), ('ZZ1', 2.5), ('CONSIGN', 40)) v (code, qty)
            JOIN bc_warehouse w ON w.warehouse_code = v.code
            """,
            new { Item = stocked },
            uow.Transaction);

        var sellable = await db.Get<IInventoryRepository>().GetSellableOnHandAsync([stocked, empty], uow.Transaction, None);

        sellable.Should().BeEquivalentTo(new Dictionary<long, decimal> { [stocked] = 7.5m, [empty] = 0m });
    }

    private static Task<int> InsertWarehousesAsync(IUnitOfWork uow) =>
        uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO bc_warehouse (warehouse_code, warehouse_name, use_yn, business_place_id, created_by, updated_by)
            SELECT w.code, w.name, w.use_yn, p.id, 't', 't'
            FROM (VALUES ('ZZ1', 'Last', true), ('AA1', 'First', true), ('OFF1', 'Closed', false)) w (code, name, use_yn)
            CROSS JOIN bc_business_place p WHERE p.is_default
            """,
            transaction: uow.Transaction);
}
