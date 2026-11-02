using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Items;

/// <summary>Alternate item group members against PostgreSQL (BC-UC-012). Each test rolls back by not committing.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class AlternateItemRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private const string Seed = """
        INSERT INTO bc_alternate_item_group (alt_item_group_cd, alt_item_group_name, class, updated_at, created_by, updated_by)
        VALUES ('ZG1', 'Bolts', 'X', 'epoch', 't', 't'), ('ZG2', 'Nuts', 'X', 'epoch', 't', 't');
        INSERT INTO bc_item (item_cd, item_name, type, valid_flag, alternate_item_group_id, created_by, updated_by) VALUES
            ('ZA2', 'Bolt 2', 'INVENTORY', 'Y', (SELECT id FROM bc_alternate_item_group WHERE alt_item_group_cd = 'ZG1'), 't', 't'),
            ('ZA1', 'Bolt 1', 'INVENTORY', 'Y', (SELECT id FROM bc_alternate_item_group WHERE alt_item_group_cd = 'ZG1'), 't', 't'),
            ('ZB1', 'Nut 1', 'INVENTORY', 'Y', (SELECT id FROM bc_alternate_item_group WHERE alt_item_group_cd = 'ZG2'), 't', 't'),
            ('ZC1', 'Free', 'SERVICE', 'N', NULL, 't', 't');
        """;

    [DatabaseFact, Trait("Id", "BC-UC-012-IT-01"), Trait("Rule", "MM-BR-811")]
    public async Task FindAndSave_MoveItemsBetweenGroups_AndTouchBothGroups()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IAlternateItemRepository>();
        var connection = uow.Transaction.Connection!;
        await connection.ExecuteAsync(Seed, transaction: uow.Transaction);

        var g1 = (await repository.FindGroupByCodeAsync("ZG1", uow.Transaction, None))!;
        var locked = await repository.FindGroupAsync(g1.Id, forUpdate: true, uow.Transaction, None);
        var members = await repository.FindMembersAsync(g1.Id, uow.Transaction, None);
        var candidates = await repository.FindCandidatesAsync(["ZB1", "ZC1", "ZZ9"], uow.Transaction, None);
        var za1 = members[0].Id;
        var zb1 = candidates.Single(c => c.ItemCd == "ZB1");
        await repository.SaveAsync(g1.Id, [zb1.Id], [za1], "kim", uow.Transaction, None);
        var after = await repository.FindMembersAsync(g1.Id, uow.Transaction, None);
        var touched = (await connection.QueryAsync<string>(
            "SELECT alt_item_group_cd FROM bc_alternate_item_group WHERE updated_by = 'kim' ORDER BY 1", transaction: uow.Transaction)).ToList();
        var za1Group = await connection.ExecuteScalarAsync<long?>(
            "SELECT alternate_item_group_id FROM bc_item WHERE id = @Id", new { Id = za1 }, uow.Transaction);

        g1.Should().BeEquivalentTo(new { AltItemGroupCd = "ZG1", AltItemGroupName = "Bolts", UpdatedAt = DateTimeOffset.UnixEpoch });
        locked.Should().Be(g1);
        members.Select(m => (m.ItemCd, m.ItemName)).Should().Equal(("ZA1", "Bolt 1"), ("ZA2", "Bolt 2"));
        candidates.Select(c => (c.ItemCd, c.ItemType, c.ValidFlag, c.GroupCd)).Should().BeEquivalentTo(
            [("ZB1", "INVENTORY", "Y", "ZG2"), ("ZC1", "SERVICE", "N", (string?)null)]);
        after.Select(m => m.ItemCd).Should().Equal("ZA2", "ZB1");
        touched.Should().Equal("ZG1", "ZG2");
        za1Group.Should().BeNull();
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bc_item WHERE item_cd = 'ZA1'", transaction: uow.Transaction))
            .Should().Be(1, "an item that leaves the group is not deleted");
    }

    [DatabaseFact, Trait("Id", "MM-UT-814"), Trait("Rule", "MM-BR-814")]
    public async Task GroupCodeChange_KeepsItsItems_BecauseItemsPointToTheGroupById()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IAlternateItemRepository>();
        var connection = uow.Transaction.Connection!;
        await connection.ExecuteAsync(Seed, transaction: uow.Transaction);

        await connection.ExecuteAsync(
            "UPDATE bc_alternate_item_group SET alt_item_group_cd = 'ZG9' WHERE alt_item_group_cd = 'ZG1'", transaction: uow.Transaction);
        var renamed = (await repository.FindGroupByCodeAsync("ZG9", uow.Transaction, None))!;
        var old = await repository.FindGroupByCodeAsync("ZG1", uow.Transaction, None);

        old.Should().BeNull();
        (await repository.FindMembersAsync(renamed.Id, uow.Transaction, None)).Select(m => m.ItemCd).Should().Equal("ZA1", "ZA2");
    }
}
