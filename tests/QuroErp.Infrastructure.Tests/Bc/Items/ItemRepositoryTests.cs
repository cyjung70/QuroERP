using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Items;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Items;

/// <summary>Item master reads and writes (BC-UC-009) on real PostgreSQL. Each test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class ItemRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly ItemFields Fields = new(
        "HEX BOLT", "M8", null, "INVENTORY", "EA", "PART", "Y", 2.5m, 4m, "0123456789", "A-1", 10, "m1", null, null, null, "m5", null, false, "EXEMPT");

    [DatabaseFact, Trait("Id", "BC-UC-009-IT-01")]
    public async Task Insert_ThenFind_StoresValuesCostAndGroup()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();

        var id = await repository.InsertAsync(Item.Create("IT-B100", Fields), "kim", uow.Transaction, None);
        var row = await repository.FindAsync(id, forUpdate: true, uow.Transaction, None);

        row.Should().BeEquivalentTo(new
        {
            ItemCd = "IT-B100", ItemName = "HEX BOLT", ItemType = "INVENTORY", ItemClass = "PART", UsdPrc = 2.5m, SoUsdPrc = 4m,
            ReorderPoint = 10m, Memo5 = "m5", Taxable = false, VatKind = "EXEMPT", Plating = "R", ItemGroupCd = "IT-B100",
            MvgCost = 2.5m, OnhandQty = 0m, StkVal = 0m,
        });
        (await repository.CodeExistsAsync("IT-B100", uow.Transaction, None)).Should().BeTrue();
        (await repository.CodeExistsAsync("IT-B101", uow.Transaction, None)).Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "BC-UC-009-IT-02")]
    public async Task Update_ChecksUpdatedAt_AndKeepsCost()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        var id = await repository.InsertAsync(Item.Create("IT-B100", Fields), "kim", uow.Transaction, None);
        var row = (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None))!;
        var item = Item.Restore(id, row.ItemCd, Fields, row.Plating, row.MvgCost!.Value, 0, 0);
        item.Change(Fields with { ItemName = "NUT", UsdPrc = 9m }, typeLocked: false);

        var stale = await repository.UpdateAsync(item, row.UpdatedAt.AddSeconds(-1), "lee", uow.Transaction, None);
        var saved = await repository.UpdateAsync(item, row.UpdatedAt, "lee", uow.Transaction, None);
        var after = await repository.FindAsync(id, forUpdate: false, uow.Transaction, None);

        stale.Should().BeFalse();
        saved.Should().BeTrue();
        after!.ItemName.Should().Be("NUT");
        after.UsdPrc.Should().Be(9m);
        after.MvgCost.Should().Be(2.5m);
    }

    [DatabaseFact, Trait("Id", "BC-UC-009-IT-03")]
    public async Task Search_FiltersByCodePrefixNameClassAndFlag_PagesInClassCodeOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        await repository.InsertAsync(Item.Create("ITX_2", Fields with { ItemClass = "B" }), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ITX_1", Fields with { ItemClass = "B" }), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ITX_3", Fields with { ItemClass = "A", ItemName = "WASHER" }), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ITXA9", Fields with { ValidFlag = "N" }), "kim", uow.Transaction, None);

        var all = await repository.SearchAsync(new ItemSearch("itx_", null, null, null, 0, 2), uow.Transaction, None);
        var second = await repository.SearchAsync(new ItemSearch("ITX_", null, null, null, 2, 2), uow.Transaction, None);
        var byName = await repository.SearchAsync(new ItemSearch("ITX", "wash", null, null, 0, 50), uow.Transaction, None);
        var byClassAndFlag = await repository.SearchAsync(new ItemSearch("ITX", null, "PART", "N", 0, 50), uow.Transaction, None);

        all.Total.Should().Be(3);
        all.Items.Select(i => i.ItemCd).Should().Equal("ITX_3", "ITX_1");
        second.Items.Select(i => i.ItemCd).Should().Equal("ITX_2");
        byName.Items.Select(i => i.ItemCd).Should().Equal("ITX_3");
        byClassAndFlag.Items.Select(i => i.ItemCd).Should().Equal("ITXA9");
    }

    [DatabaseFact, Trait("Id", "BC-UC-009-IT-04")]
    public async Task IsTypeLocked_ByStockMovementOrSetStructure()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        var free = await repository.InsertAsync(Item.Create("IT-FREE", Fields), "kim", uow.Transaction, None);
        var parent = await repository.InsertAsync(Item.Create("IT-SET", Fields with { ItemType = "SET" }), "kim", uow.Transaction, None);
        var child = await repository.InsertAsync(Item.Create("IT-PART", Fields), "kim", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync(
            "INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by) VALUES (@Parent, 1, @Child, 1, 's', 's')",
            new { Parent = parent, Child = child }, uow.Transaction);

        (await repository.IsTypeLockedAsync(free, uow.Transaction, None)).Should().BeFalse();
        (await repository.IsTypeLockedAsync(parent, uow.Transaction, None)).Should().BeTrue();
        (await repository.IsTypeLockedAsync(child, uow.Transaction, None)).Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "BC-UC-009-IT-05")]
    public async Task IsInUse_ByDocumentLine_DeleteRemovesUnusedItem()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        var used = await repository.InsertAsync(Item.Create("IT-USED", Fields), "kim", uow.Transaction, None);
        var unused = await repository.InsertAsync(Item.Create("IT-UNUSED", Fields), "kim", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync(
            """
            WITH po AS (
                INSERT INTO mm_purchase_order (po_no, created_by, updated_by) VALUES ('PO-IT', 's', 's') RETURNING id)
            INSERT INTO mm_purchase_order_line (purchase_order_id, seq, item_id, created_by, updated_by) SELECT id, 1, @Item, 's', 's' FROM po
            """,
            new { Item = used }, uow.Transaction);
        var row = (await repository.FindAsync(unused, forUpdate: false, uow.Transaction, None))!;

        (await repository.IsInUseAsync(used, uow.Transaction, None)).Should().BeTrue();
        (await repository.IsInUseAsync(unused, uow.Transaction, None)).Should().BeFalse();
        (await repository.DeleteAsync(unused, row.UpdatedAt.AddSeconds(-1), uow.Transaction, None)).Should().BeFalse();
        (await repository.DeleteAsync(unused, row.UpdatedAt, uow.Transaction, None)).Should().BeTrue();
        (await repository.FindAsync(unused, forUpdate: false, uow.Transaction, None)).Should().BeNull();
    }
}
