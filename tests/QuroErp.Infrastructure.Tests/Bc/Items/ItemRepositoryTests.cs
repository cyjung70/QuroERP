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

    // No barcode: tests insert several items and barcodes are unique (MM-BR-817)
    private static readonly ItemFields Fields = new(
        "HEX BOLT", "M8", null, "INVENTORY", "EA", "PART", "Y", 2.5m, 4m, null, "A-1", 10, "m1", null, null, null, "m5", null, false, "EXEMPT");

    [DatabaseFact, Trait("Id", "BC-UC-009-IT-01")]
    public async Task Insert_ThenFind_StoresValuesCostAndGroup()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();

        var id = await repository.InsertAsync(Item.Create("IT-B100", Fields), "kim", uow.Transaction, None);
        var row = await repository.FindAsync(id, forUpdate: true, uow.Transaction, None);

        row.Should().BeEquivalentTo(new
        {
            ItemCd = "IT-B100",
            ItemName = "HEX BOLT",
            ItemType = "INVENTORY",
            ItemClass = "PART",
            UsdPrc = 2.5m,
            SoUsdPrc = 4m,
            ReorderPoint = 10m,
            Memo5 = "m5",
            Taxable = false,
            VatKind = "EXEMPT",
            Plating = "R",
            ItemGroupCd = "IT-B100",
            MvgCost = 2.5m,
            OnhandQty = 0m,
            StkVal = 0m,
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

    [DatabaseFact, Trait("Id", "CORE-050-IT-11")]
    public async Task Find_ReturnsTheFourAuditColumns_UpdateChangesOnlyTheUpdatedPair()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        var id = await repository.InsertAsync(Item.Create("IT-AUD1", Fields), "kim", uow.Transaction, None);
        var row = (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None))!;
        var item = Item.Restore(id, row.ItemCd, Fields, row.Plating, row.MvgCost!.Value, 0, 0);
        item.Change(Fields with { ItemName = "NUT" }, typeLocked: false);

        await repository.UpdateAsync(item, row.UpdatedAt, "lee", uow.Transaction, None);
        var after = (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None))!;

        (row.CreatedBy, row.UpdatedBy).Should().Be(("kim", "kim"));
        row.CreatedAt.Should().NotBe(default).And.Be(row.UpdatedAt);
        (after.CreatedAt, after.CreatedBy, after.UpdatedBy).Should().Be((row.CreatedAt, "kim", "lee"));
    }

    [DatabaseFact, Trait("Id", "CORE-020-IT-01")]
    public async Task FindAdjacent_FollowsClassThenCode_WithoutClassFirst()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        // Item class is required now (MM-E-802); a row without one can still come from migrated data
        var noClass = await repository.InsertAsync(Item.Create("ZZZZ0", Fields), "kim", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync(
            "UPDATE bc_item SET class = NULL WHERE id = @Id", new { Id = noClass }, transaction: uow.Transaction);
        var a = await repository.InsertAsync(Item.Create("NAV2", Fields with { ItemClass = "ZZZQ1" }), "kim", uow.Transaction, None);
        var b = await repository.InsertAsync(Item.Create("NAV1", Fields with { ItemClass = "ZZZQ2" }), "kim", uow.Transaction, None);

        (await repository.FindAdjacentAsync(a, forward: true, uow.Transaction, None))!.Id.Should().Be(b);
        (await repository.FindAdjacentAsync(b, forward: false, uow.Transaction, None))!.Id.Should().Be(a);
        (await repository.FindAdjacentAsync(b, forward: true, uow.Transaction, None)).Should().BeNull();
        (await repository.FindAdjacentAsync(noClass, forward: true, uow.Transaction, None))!.ItemClass.Should().NotBeNull();
        (await repository.FindAdjacentAsync(-1, forward: true, uow.Transaction, None)).Should().BeNull();
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
                INSERT INTO mm_purchase_order (po_no, currency_code, exchange_rate, created_by, updated_by) VALUES ('PO-IT', 'USD', 1, 's', 's') RETURNING id)
            INSERT INTO mm_purchase_order_line (purchase_order_id, seq, item_id, tax_code_id, created_by, updated_by)
            SELECT id, 1, @Item, (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), 's', 's' FROM po
            """,
            new { Item = used }, uow.Transaction);
        var row = (await repository.FindAsync(unused, forUpdate: false, uow.Transaction, None))!;

        (await repository.IsInUseAsync(used, uow.Transaction, None)).Should().BeTrue();
        (await repository.IsInUseAsync(unused, uow.Transaction, None)).Should().BeFalse();
        (await repository.DeleteAsync(unused, row.UpdatedAt.AddSeconds(-1), uow.Transaction, None)).Should().BeFalse();
        (await repository.DeleteAsync(unused, row.UpdatedAt, uow.Transaction, None)).Should().BeTrue();
        (await repository.FindAsync(unused, forUpdate: false, uow.Transaction, None)).Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "BC-UC-010-T01"), Trait("Id", "BC-UC-010-T03"), Trait("Id", "MM-UT-820"), Trait("Id", "MM-UT-822"), Trait("Id", "MM-UT-823")]
    public async Task Image_ReplacedWhole_FallsBackToTheItemGroupInPlatingThenCodeOrder_AndKeepsUpdatedAt()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        byte[] large = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6];
        byte[] small = [0xFF, 0xD8, 0xFF, 0xDB];
        byte[] other = [0xFF, 0xD8, 0xFF, 0xE1, 7];
        // Plating other than R puts the item in the group of its code without the last letter (MM-BR-808)
        var cover = await repository.InsertAsync(Item.Create("IMGG", Fields), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Restore(0, "IMGGX", Fields, "X", 1m, 0, 0), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Restore(0, "IMGGA", Fields, "A", 1m, 0, 0), "kim", uow.Transaction, None);
        var before = (await repository.FindAsync(cover, forUpdate: false, uow.Transaction, None))!;

        await repository.SaveImageAsync("IMGG", large, uow.Transaction, None);
        await repository.SaveImageAsync("IMGG", small, uow.Transaction, None);
        await repository.SaveImageAsync("IMGGA", other, uow.Transaction, None);
        var own = await repository.FindImageAsync("IMGG", uow.Transaction, None);
        var fromCover = await repository.FindImageAsync("IMGGX", uow.Transaction, None);
        // The legacy Clear left an empty image, which counts as none
        await uow.Transaction.Connection!.ExecuteAsync("UPDATE bc_item SET img = ''::bytea WHERE item_cd = 'IMGG'", transaction: uow.Transaction);
        var fromNextByCode = await repository.FindImageAsync("IMGGX", uow.Transaction, None);
        var coverFromMember = await repository.FindImageAsync("IMGG", uow.Transaction, None);
        await repository.SaveImageAsync("IMGGA", null, uow.Transaction, None);
        var none = await repository.FindImageAsync("IMGGX", uow.Transaction, None);
        var after = (await repository.FindAsync(cover, forUpdate: false, uow.Transaction, None))!;

        own.Should().Equal(small);
        fromCover.Should().Equal(small);
        fromNextByCode.Should().Equal(other);
        coverFromMember.Should().Equal(other);
        none.Should().BeNull();
        after.UpdatedAt.Should().Be(before.UpdatedAt);
        (await repository.SaveImageAsync("IMG-NONE", small, uow.Transaction, None)).Should().BeFalse();
        (await repository.FindImageAsync("IMG-NONE", uow.Transaction, None)).Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "BC-UC-011-T01"), Trait("Id", "MM-UT-815")]
    public async Task ListForBarcode_ItemClassAndValidFlag_InOrdinalItemCodeOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        var barcodeFields = Fields with { ItemClass = "ZZBC1" };
        await repository.InsertAsync(Item.Create("ZBC2", barcodeFields), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ZBC10", barcodeFields with { UpcCode = "9100000001" }), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ZBC-1", barcodeFields), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ZBC3", barcodeFields with { ValidFlag = "N" }), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ZBC4", Fields with { ItemClass = "ZZBC2" }), "kim", uow.Transaction, None);

        var inUse = await repository.ListForBarcodeAsync("ZZBC1", "Y", forUpdate: true, uow.Transaction, None);
        var all = await repository.ListForBarcodeAsync("ZZBC1", null, forUpdate: false, uow.Transaction, None);

        inUse.Select(r => r.ItemCd).Should().Equal("ZBC-1", "ZBC10", "ZBC2");
        inUse[1].Should().BeEquivalentTo(new { ItemName = "HEX BOLT", SoUsdPrc = 4m, UpcCode = "9100000001" });
        all.Select(r => r.ItemCd).Should().Equal("ZBC-1", "ZBC10", "ZBC2", "ZBC3");
    }

    [DatabaseFact, Trait("Id", "BC-UC-011-T02"), Trait("Id", "MM-UT-817")]
    public async Task SaveBarcodes_MovesBarcodesBetweenTheGivenItems_AndOwnersSkipTheExceptedItems()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        var a = await repository.InsertAsync(Item.Create("ZBCA", Fields with { UpcCode = "9200000001" }), "kim", uow.Transaction, None);
        var b = await repository.InsertAsync(Item.Create("ZBCB", Fields with { UpcCode = "9200000002" }), "kim", uow.Transaction, None);

        await repository.SaveBarcodesAsync([(a, "9200000002"), (b, "9200000001")], "lee", uow.Transaction, None);

        (await repository.FindAsync(a, forUpdate: false, uow.Transaction, None))!.UpcCode.Should().Be("9200000002");
        (await repository.FindAsync(b, forUpdate: false, uow.Transaction, None))!.UpcCode.Should().Be("9200000001");
        (await repository.FindBarcodeOwnersAsync(["9200000001", "9200000002", "9200000003"], [a], uow.Transaction, None))
            .Should().Equal(new ItemBarcodeOwner("9200000001", "ZBCB"));
        (await repository.FindBarcodeOwnersAsync(["9200000001"], [a, b], uow.Transaction, None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "BC-UC-011-T05"), Trait("Id", "MM-UT-818")]
    public async Task SaveBarcodes_OneBarcodeTakenByAnotherItem_ChangesNothing()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IItemRepository>();
        var a = await repository.InsertAsync(Item.Create("ZBCC", Fields with { UpcCode = "9300000001" }), "kim", uow.Transaction, None);
        var b = await repository.InsertAsync(Item.Create("ZBCD", Fields), "kim", uow.Transaction, None);
        await repository.InsertAsync(Item.Create("ZBCE", Fields with { UpcCode = "9300000003" }), "kim", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync("SAVEPOINT barcodes", transaction: uow.Transaction);

        var save = () => repository.SaveBarcodesAsync([(a, "9300000002"), (b, "9300000003")], "lee", uow.Transaction, None);

        (await save.Should().ThrowAsync<Npgsql.PostgresException>()).Which.ConstraintName.Should().Be("uq_bc_item_upc_code");
        await uow.Transaction.Connection!.ExecuteAsync("ROLLBACK TO SAVEPOINT barcodes", transaction: uow.Transaction);
        (await repository.FindAsync(a, forUpdate: false, uow.Transaction, None))!.UpcCode.Should().Be("9300000001");
        (await repository.FindAsync(b, forUpdate: false, uow.Transaction, None))!.UpcCode.Should().BeNull();
    }
}
