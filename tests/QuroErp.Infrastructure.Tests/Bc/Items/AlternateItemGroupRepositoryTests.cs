using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Items;

/// <summary>Alternate item groups against PostgreSQL (BC-UC-019). Each test rolls back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class AlternateItemGroupRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "BC-UC-019-IT-01"), Trait("Rule", "MM-BR-810")]
    public async Task InsertSearchSwapCodesAndDelete_RoundTrip()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IAlternateItemGroupRepository>();
        var connection = uow.Transaction.Connection!;

        await repository.LockAsync(uow.Transaction, None);
        await repository.InsertAsync([new("ZG1", "Bolt_%"), new("ZG2", "Nuts")], "ZZCLS", "kim", uow.Transaction, None);
        await connection.ExecuteAsync(
            "INSERT INTO bc_item (item_cd, alternate_item_group_id, created_by, updated_by) SELECT 'ZI1', id, 't', 't' FROM bc_alternate_item_group WHERE alt_item_group_cd = 'ZG1'",
            transaction: uow.Transaction);
        var inserted = await repository.SearchAsync("ZZCLS", null, 200, uow.Transaction, None);
        var byKeyword = await repository.SearchAsync(null, "t_%", 200, uow.Transaction, None);
        var owners = await repository.FindCodeOwnersAsync(["ZG2", "ZZ9"], uow.Transaction, None);
        var ids = inserted.Select(g => g.Id).ToList();
        var locked = await repository.FindForUpdateAsync(ids, uow.Transaction, None);
        await repository.UpdateAsync([(ids[0], new("ZG2", "Bolts")), (ids[1], new("ZG1", "Nuts"))], "lee", uow.Transaction, None);
        var swapped = await repository.SearchAsync("ZZCLS", null, 200, uow.Transaction, None);
        await repository.DeleteAsync([ids[1]], uow.Transaction, None);
        var left = await repository.SearchAsync("ZZCLS", null, 200, uow.Transaction, None);

        inserted.Select(g => (g.AltItemGroupCd, g.AltItemGroupName, g.ItemClass, g.ItemCount)).Should().Equal(
            ("ZG1", "Bolt_%", "ZZCLS", 1L), ("ZG2", "Nuts", "ZZCLS", 0L));
        byKeyword.Select(g => g.AltItemGroupCd).Should().Equal("ZG1");
        owners.Select(o => o.Code).Should().Equal("ZG2");
        locked.Should().HaveCount(2);
        swapped.Select(g => (g.Id, g.AltItemGroupCd, g.ItemCount)).Should().Equal((ids[1], "ZG1", 0L), (ids[0], "ZG2", 1L));
        left.Select(g => g.AltItemGroupCd).Should().Equal("ZG2");
    }
}
