using Dapper;
using FluentAssertions;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Warehouses;

/// <summary>Warehouse choice list on real PostgreSQL (TASK-CORE-022). Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class WarehouseRepositoryTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "CORE-022-IT-01")]
    public async Task FindOptions_ReturnsWarehousesInUse_InCodeOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO bc_warehouse (warehouse_code, warehouse_name, use_yn, business_place_id, created_by, updated_by)
            SELECT w.code, w.name, w.use_yn, p.id, 't', 't'
            FROM (VALUES ('ZZ1', 'Last', true), ('AA1', 'First', true), ('OFF1', 'Closed', false)) w (code, name, use_yn)
            CROSS JOIN bc_business_place p WHERE p.is_default
            """,
            transaction: uow.Transaction);

        var options = await db.Get<IWarehouseRepository>().FindOptionsAsync(uow.Transaction, CancellationToken.None);

        options.Select(o => o.WarehouseCode).Should().Equal("AA1", "MAIN", "ZZ1");
        options.Should().ContainSingle(o => o.WarehouseCode == "MAIN").Which.WarehouseName.Should().Be("Main");
        options.Should().OnlyContain(o => o.Id > 0);
    }
}
