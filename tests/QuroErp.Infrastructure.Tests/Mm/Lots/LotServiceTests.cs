using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Labels;
using QuroErp.Application.Mm.Lots;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Lots;

/// <summary>Own schema for lot tests: the item and inventory services commit, so their rows must not reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class LotSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "LotSchema";
}

/// <summary>
/// 0404_mm_lot and 0418 (Location and lot balances on mm_onhand), item lot and Location management, postings, lot search,
/// FIFO suggestions and the per-key reconciliation on real PostgreSQL (TASK-CORE-027, TASK-CORE-067, TASK-CORE-068,
/// MM-BR-351~365, MM-BR-605, MM-BR-713~718).
/// Lot management starts on <see cref="Today"/>; each test makes its own items and a second warehouse.
/// </summary>
[Collection(LotSchemaDefinition.Name)]
public class LotServiceTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly DateOnly Before = new(2026, 9, 20);
    private static readonly DateOnly Day = new(2026, 10, 10);

    private ItemService Items => new(
        db.Get<IItemRepository>(), db.Get<ILotRepository>(), new AnyCode(), db.Get<IUnitOfWorkFactory>(), new FixedClock());

    private LotService Lots => new(db.Get<ILotRepository>(), db.Get<IUnitOfWorkFactory>(), new FixedClock());

    [DatabaseFact, Trait("Id", "MM-UT-351"), Trait("Rule", "MM-BR-351")]
    public async Task TurningOn_KeepsEachWarehouseStockOnLotStar_AndStartsToday()
    {
        var (main, second) = await WarehousesAsync();
        var item = await CreateAsync();
        await PostAsync(new("GR", Before, item.Id, main, 10, Price: 2), new("GR", Before, item.Id, second, 4, Price: 2));

        var saved = await Items.UpdateAsync(item.Id, Update(await Items.GetAsync(item.Id, None), lotManaged: true), "kim", None);

        (saved.LotManaged, saved.LotManagedFrom).Should().Be((true, Today));
        (await Lots.SearchAsync(Query(item.Id, main, Today), None)).Should().Equal(
            new LotBalanceDto(Lot(await LotsAsync(item.Id), "*"), "*", 10, await StarLocationAsync(main), "*"));
        (await Lots.SearchAsync(Query(item.Id, second, Today), None)).Single().OnHandQty.Should().Be(4);
        (await MismatchesAsync(item.Id)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "MM-UT-351"), Trait("Id", "MM-UT-361"), Trait("Rule", "MM-BR-361")]
    public async Task TurningOn_IsRefused_ForAServiceItem_ButAllowedForANegativeWarehouse_AndAKitComponent()
    {
        var (main, _) = await WarehousesAsync();
        var negative = await CreateAsync();
        await ExecuteAsync(
            $"""
            INSERT INTO mm_onhand (item_id, warehouse_id, location_id, lot_id, onhand_qty, created_by, updated_by)
            VALUES (@Id, @Main, {StarKeys.Location("@Main")}, {StarKeys.Lot("@Id")}, -1, 't', 't')
            """,
            new { negative.Id, Main = main });
        var component = await CreateAsync();
        var kit = await CreateAsync(type: "KIT");
        await ExecuteAsync(
            "INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by) VALUES (@Set, 1, @Component, 1, 't', 't')",
            new { Set = kit.Id, Component = component.Id });

        var onNegative = () => Items.UpdateAsync(negative.Id, Update(negative, lotManaged: true), "kim", None);
        var onComponent = () => Items.UpdateAsync(component.Id, Update(component, lotManaged: true), "kim", None);
        var onService = () => CreateAsync(type: "SERVICE", lotManaged: true);

        // The stock stays on lot '*', so a warehouse below zero does not matter (MM-BR-351)
        (await onNegative()).LotManaged.Should().BeTrue();
        (await onService.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.ManagementCode);
        // MM-BR-361: a Kit or Set component can be lot-managed
        (await onComponent()).LotManaged.Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "MM-UT-351"), Trait("Id", "MM-UT-807")]
    public async Task TurningOn_WithoutStock_KeepsOnlyLotStar_AndAnItemWithLotsIsInUse()
    {
        var (main, _) = await WarehousesAsync();
        var empty = await CreateAsync();
        var used = await CreateAsync(lotManaged: true);
        await PostAsync(new StockMovementRequest("GR", Day, used.Id, main, 1, Price: 2, LotNo: "B1"));
        // Only the lot row refers to the item now
        await ExecuteAsync("DELETE FROM mm_goods_movement WHERE item_id = @Id", new { used.Id });

        await Items.UpdateAsync(empty.Id, Update(empty, lotManaged: true), "kim", None);

        (await LotsAsync(empty.Id)).Keys.Should().Equal("*");
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        (await db.Get<IItemRepository>().IsInUseAsync(empty.Id, uow.Transaction, None)).Should().BeFalse();
        (await db.Get<IItemRepository>().IsInUseAsync(used.Id, uow.Transaction, None)).Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "MM-UT-351"), Trait("Rule", "MM-BR-351")]
    public async Task TurningOff_NeedsEveryLotBalanceAtZero()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true);
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2, LotNo: "B1"));

        var current = await Items.GetAsync(item.Id, None);
        var withStock = () => Items.UpdateAsync(item.Id, Update(current, lotManaged: false), "kim", None);
        (await withStock.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.ManagementCode);

        await PostAsync(new StockMovementRequest("DN", Day, item.Id, main, -5, LotNo: "B1"));
        var off = await Items.UpdateAsync(item.Id, Update(await Items.GetAsync(item.Id, None), lotManaged: false), "kim", None);

        (off.LotManaged, off.LotManagedFrom).Should().Be((false, (DateOnly?)null));
    }

    [DatabaseFact, Trait("Id", "MM-UT-354"), Trait("Rule", "MM-BR-354"), Trait("Rule", "MM-BR-353"), Trait("Rule", "MM-BR-355")]
    public async Task Postings_KeepTheLotSumEqualToOnHand_AndAReversalKeepsItsLot()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true);

        var receipts = await PostAsync(
            new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2), new StockMovementRequest("GR", Day, item.Id, main, 3, Price: 2));
        var issue = await PostAsync(new StockMovementRequest("DN", Day, item.Id, main, -2, LotNo: "261010-001"));
        await ReverseAsync(issue.Single().Id);

        var lots = await LotsAsync(item.Id);
        lots.Keys.Should().Equal("*", "261010-001", "261010-002");
        receipts.Select(m => m.LotId).Should().Equal(lots["261010-001"], lots["261010-002"]);
        (await QueryAsync<long?>("SELECT lot_id FROM mm_goods_movement WHERE item_id = @Id ORDER BY id", new { item.Id }))
            .Should().Equal(lots["261010-001"], lots["261010-002"], lots["261010-001"], lots["261010-001"]);
        // The lot balances are the item + warehouse rows of mm_onhand; nothing is left on Lot '*' (MM-BR-354, MM-BR-714)
        (await LotOnHandAsync(item.Id)).Should().Equal(("*", 0m), ("261010-001", 5m), ("261010-002", 3m));
        (await MismatchesAsync(item.Id)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task Issue_AboveTheLotBalanceOnItsDate_IsMmE356_AndSavesNothing()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true);
        // Lot A keeps the warehouse stock positive on 10/9, so only the lot check can stop the issue
        await PostAsync(
            new StockMovementRequest("GR", Today, item.Id, main, 5, Price: 2, LotNo: "A"),
            new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2, LotNo: "B1"));

        var act = () => PostAsync(new StockMovementRequest("DN", Day.AddDays(-1), item.Id, main, -1, LotNo: "B1"));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        (error.Code, error.Args[4], error.Args[5]).Should().Be((LotRules.NegativeCode, (object)"2026-10-09", (object)(-1m)));
        (await LotOnHandAsync(item.Id)).Should().Equal(("*", 0m), ("A", 5m), ("B1", 5m));
    }

    [DatabaseFact, Trait("Id", "MM-UT-362"), Trait("Rule", "MM-BR-362")]
    public async Task Search_ShowsLotsAboveZeroAtTheEndOfTheDate_InFifoOrder()
    {
        var (main, second) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true);
        await PostAsync(
            new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2, LotNo: "C"),
            new StockMovementRequest("GR", Day, item.Id, main, 2, Price: 2, LotNo: "A"),
            new StockMovementRequest("GR", Day.AddDays(5), item.Id, main, 4, Price: 2, LotNo: "B"),
            new StockMovementRequest("GR", Day, item.Id, second, 9, Price: 2, LotNo: "D"));
        await PostAsync(new StockMovementRequest("DN", Day.AddDays(1), item.Id, main, -2, LotNo: "A"));

        var onDay = await Lots.SearchAsync(Query(item.Id, main, Day), None);
        var later = await Lots.SearchAsync(Query(item.Id, main, Day.AddDays(5)), None);
        var missing = () => Lots.SearchAsync(new LotSearchQuery { ItemId = item.Id, WarehouseId = main }, None);

        // A and C were received on the same day, B five days later (MM-BR-364, MM-BR-716)
        onDay.Select(l => (l.LotNo, l.OnHandQty, l.LocationCode)).Should().Equal(("A", 2m, "*"), ("C", 5m, "*"));
        later.Select(l => (l.LotNo, l.OnHandQty)).Should().Equal(("C", 5m), ("B", 4m));
        (await missing.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("date");
    }

    [DatabaseFact, Trait("Id", "MM-UT-352"), Trait("Rule", "MM-BR-352")]
    public async Task Migration_RejectsBadLotNumbers_AndDuplicateLots()
    {
        var item = await CreateAsync(lotManaged: true);
        await ExecuteAsync("INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by) VALUES (@Id, 'OK-1', 't', 't')", new { item.Id });

        var lower = () => ExecuteAsync("INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by) VALUES (@Id, 'ok-2', 't', 't')", new { item.Id });
        var duplicate = () => ExecuteAsync("INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by) VALUES (@Id, 'OK-1', 't', 't')", new { item.Id });
        var secondStar = () => ExecuteAsync("INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by) VALUES (@Id, '*', 't', 't')", new { item.Id });

        (await lower.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_mm_lot_lot_no");
        (await duplicate.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("uq_mm_lot_item_id_lot_no");
        (await secondStar.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("uq_mm_lot_item_id_lot_no",
            "the item got its system lot '*' when it was created (MM-BR-363)");
    }

    [DatabaseFact, Trait("Id", "MM-UT-353"), Trait("Rule", "MM-BR-353"), Trait("Rule", "MM-BR-364")]
    public async Task PurchaseReceipt_NumbersANewLotWithItsReceivedDate_OtherReceiptsUseLotStar()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true);

        var posted = await PostAsync(
            new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2),
            new StockMovementRequest("UR", Day, item.Id, main, 2, Price: 2));

        var lots = await LotsAsync(item.Id);
        posted.Select(m => m.LotId).Should().Equal(lots["261010-001"], lots["*"]);
        (await QueryAsync<DateOnly?>("SELECT received_dt FROM mm_lot WHERE id = @Id", new { Id = lots["261010-001"] })).Single()
            .Should().Be(Day);
    }

    [DatabaseFact, Trait("Id", "MM-UT-716"), Trait("Rule", "MM-BR-716"), Trait("Rule", "MM-BR-713")]
    public async Task Suggest_FillsFromLotStarThenTheOldestLots_OverLocationsInUse()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true, locationManaged: true);
        var (a01, a01Code) = await AddLocationAsync(main, "A01");
        var (b01, b01Code) = await AddLocationAsync(main, "B01");
        var (closed, _) = await AddLocationAsync(main, "Z99");
        await PostAsync(
            new StockMovementRequest("UR", Day, item.Id, main, 1, Price: 2),
            new StockMovementRequest("GR", Day, item.Id, main, 4, Price: 2, LocationId: b01),
            new StockMovementRequest("GR", Day.AddDays(-3), item.Id, main, 2, Price: 2, LocationId: a01),
            new StockMovementRequest("GR", Day.AddDays(-5), item.Id, main, 9, Price: 2, LocationId: closed));
        await ExecuteAsync("UPDATE bc_location SET use_yn = false WHERE id = @closed", new { closed });

        // The receipts are dated after the fixed clock's today, so the suggestion asks for the balances at the end of Day
        var picks = await Lots.SuggestAsync(new StockSuggestionQuery { ItemId = item.Id, WarehouseId = main, Qty = 5, Date = Day }, None);
        var noQty = () => Lots.SuggestAsync(new StockSuggestionQuery { ItemId = item.Id, WarehouseId = main, Qty = 0, Date = Day }, None);

        picks.Lines.Select(p => (p.LocationCode, p.LotNo, p.Qty)).Should().Equal(("*", "*", 1m), (a01Code, "261007-001", 2m), (b01Code, "261010-001", 2m));
        picks.Lines[1].LocationId.Should().Be(a01);
        picks.ShortQty.Should().Be(0);
        (await noQty.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("qty");
    }

    [DatabaseFact, Trait("Id", "MM-UT-713"), Trait("Rule", "MM-BR-713")]
    public async Task Posting_ToALocation_KeepsItsOwnBalance_AndAReversalPutsItBack()
    {
        var (main, second) = await WarehousesAsync();
        var item = await CreateAsync(locationManaged: true);
        var (a01, a01Code) = await AddLocationAsync(main, "A01");
        var (otherWarehouse, _) = await AddLocationAsync(second, "A01");

        var receipt = (await PostAsync(new StockMovementRequest("UR", Day, item.Id, main, 5, Price: 2, LocationId: a01))).Single();
        var wrong = () => PostAsync(new StockMovementRequest("UR", Day, item.Id, main, 1, Price: 2, LocationId: otherWarehouse));
        var short_ = () => PostAsync(new StockMovementRequest("DN", Day, item.Id, main, -6, LocationId: a01));

        (await KeyOnHandAsync(item.Id)).Should().Equal(("*", "*", 0m), (a01Code, "*", 5m));
        (await wrong.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(StockKeyRules.LocationCode);
        (await short_.Should().ThrowAsync<DomainException>()).Which.Code.Should().BeOneOf("MM-E-001", StockKeyRules.NegativeCode);
        await ReverseAsync(receipt.Id);
        (await KeyOnHandAsync(item.Id)).Should().Equal(("*", "*", 0m), (a01Code, "*", 0m));
        (await QueryAsync<long>("SELECT location_id FROM mm_goods_movement WHERE item_id = @Id ORDER BY id", new { item.Id }))
            .Should().Equal(a01, a01);
    }

    [DatabaseFact, Trait("Id", "MM-UT-718"), Trait("Rule", "MM-BR-718")]
    public async Task TurningLocationManagementOff_NeedsEveryBalanceOnOtherLocationsAtZero()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(locationManaged: true);
        var (a01, _) = await AddLocationAsync(main, "A01");
        await PostAsync(new StockMovementRequest("UR", Day, item.Id, main, 3, Price: 2, LocationId: a01));

        // Posting stock updates the item row, so the update sends the current updatedAt
        var current = await Items.GetAsync(item.Id, None);
        var withStock = () => Items.UpdateAsync(item.Id, Update(current, lotManaged: false, locationManaged: false), "kim", None);
        (await withStock.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(StockKeyRules.LocationManagementCode);

        await PostAsync(new StockMovementRequest("DN", Day, item.Id, main, -3, LocationId: a01));
        var off = await Items.UpdateAsync(item.Id, Update(await Items.GetAsync(item.Id, None), lotManaged: false, locationManaged: false), "kim", None);

        off.LocationManaged.Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "MM-UT-605"), Trait("Rule", "MM-BR-605")]
    public async Task Reconciliation_ComparesEveryKeyWithItsLedgerSum()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true);
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2, LotNo: "B1"));
        (await MismatchesAsync(item.Id)).Should().BeEmpty();

        // The item + warehouse total stays 5, but the stock sits on the wrong lot
        await ExecuteAsync(
            """
            UPDATE mm_onhand o SET onhand_qty = onhand_qty + CASE WHEN l.lot_no = '*' THEN 5 ELSE -5 END
            FROM mm_lot l WHERE l.id = o.lot_id AND o.item_id = @Id
            """, new { item.Id });

        (await MismatchesAsync(item.Id)).Select(r => (r.LocationCode, r.LotNo, r.OnHandQty, r.LedgerQty))
            .Should().Equal(("*", "*", 5m, 0m), ("*", "B1", 0m, 5m));
    }

    [DatabaseFact, Trait("Id", "SD-UT-518"), Trait("Rule", "SD-BR-518"), Trait("Rule", "MM-BR-007")]
    public async Task KitIssue_TakesTheLotComponentInFifoOrder_AndAReversalPutsEachLotBack()
    {
        var (main, _) = await WarehousesAsync();
        var component = await CreateAsync(lotManaged: true);
        var kit = await CreateAsync(type: "KIT");
        await ExecuteAsync(
            "INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by) VALUES (@Kit, 1, @Component, 2, 't', 't')",
            new { Kit = kit.Id, Component = component.Id });
        await PostAsync(
            new StockMovementRequest("GR", Day, component.Id, main, 5, Price: 2, LotNo: "A1"),
            new StockMovementRequest("GR", Day.AddDays(-2), component.Id, main, 2, Price: 2, LotNo: "B1"));

        var issued = await PostAsync(new StockMovementRequest("DN", Day, kit.Id, main, -2, RefKey: "DN-K", RefSeq: 1));
        var lots = await LotsAsync(component.Id);

        issued.Select(m => (m.ItemId, m.LotId, m.Qty, m.SetItemId)).Should().Equal(
            (component.Id, (long?)lots["B1"], -2m, (long?)kit.Id), (component.Id, (long?)lots["A1"], -2m, (long?)kit.Id));
        (await KeyOnHandAsync(component.Id)).Should().Equal(("*", "*", 0m), ("*", "A1", 3m), ("*", "B1", 0m));

        foreach (var movement in issued)
        {
            await ReverseAsync(movement.Id);
        }
        (await KeyOnHandAsync(component.Id)).Should().Equal(("*", "*", 0m), ("*", "A1", 5m), ("*", "B1", 2m));
        var short_ = () => PostAsync(new StockMovementRequest("DN", Day, kit.Id, main, -4));
        (await short_.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(SetComponent.NotEnoughStock);
    }

    [DatabaseFact, Trait("Id", "MM-UT-916"), Trait("Rule", "MM-BR-916"), Trait("Rule", "MM-BR-716")]
    public async Task PalletStock_IsLeftOutOfTheSuggestion_AndALineThatIsNotAPalletLineCannotTakeIt()
    {
        var (main, _) = await WarehousesAsync();
        var item = await CreateAsync(lotManaged: true);
        await PostAsync(
            new StockMovementRequest("GR", Day.AddDays(-2), item.Id, main, 4, Price: 2, LotNo: "B1"),
            new StockMovementRequest("GR", Day, item.Id, main, 6, Price: 2, LotNo: "A1"));
        var lots = await LotsAsync(item.Id);
        await ExecuteAsync(
            """
            INSERT INTO mm_pallet (pallet_no, item_id, lot_id, warehouse_id, location_id, qty, status, created_by, updated_by)
            VALUES (@No, @Item, @Lot, @Warehouse, (SELECT id FROM bc_location WHERE warehouse_id = @Warehouse AND location_code = '*'), 3, 'ACTIVE', 't', 't'),
                   (@Closed, @Item, @Lot, @Warehouse, (SELECT id FROM bc_location WHERE warehouse_id = @Warehouse AND location_code = '*'), 0, 'CLOSED', 't', 't')
            """,
            new { No = "PL" + Guid.NewGuid().ToString("N")[..8], Closed = "PL" + Guid.NewGuid().ToString("N")[..8], Item = item.Id, Lot = lots["B1"], Warehouse = main });

        var suggestion = await Lots.SuggestAsync(new StockSuggestionQuery { ItemId = item.Id, WarehouseId = main, Qty = 5, Date = Day }, None);
        var search = await Lots.SearchAsync(Query(item.Id, main, Day), None);
        var intoPallet = () => PostAsync(new StockMovementRequest("DN", Day, item.Id, main, -2, LotNo: "B1"));

        suggestion.Lines.Select(l => (l.LotNo, l.Qty)).Should().Equal(("B1", 1m), ("A1", 4m));
        search.Select(b => (b.LotNo, b.OnHandQty)).Should().Equal(("B1", 4m), ("A1", 6m));
        (await intoPallet.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal(item.ItemCd, "MAIN", "*", "B1", 2m, 3m);
        (await PostAsync(new StockMovementRequest("DN", Day, item.Id, main, -1, LotNo: "B1"))).Single().PalletId.Should().BeNull();
    }

    [DatabaseFact, Trait("Id", "MM-UT-919"), Trait("Rule", "MM-BR-919"), Trait("Rule", "MM-BR-917")]
    public async Task LabelPrint_KeepsOneHistoryRowPerLabel_AndTheFourDefaultFormatsExist()
    {
        var refId = Random.Shared.NextInt64(1_000_000, long.MaxValue);
        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None))
        {
            await db.Get<ILabelPrintRepository>().AddAsync(
                [new LabelPrintRow("PALLET", refId, 2, "ZPL", "ZEBRA-1", "kim"), new LabelPrintRow("PALLET", refId, 1, "PDF", null, "kim")],
                uow.Transaction, None);
            await uow.CommitAsync(None);
        }

        (await QueryAsync<(int, string, string?, string)>(
            "SELECT copies, output_kind, printer_name, printed_by FROM mm_label_print WHERE ref_id = @refId ORDER BY id", new { refId }))
            .Should().Equal((2, "ZPL", "ZEBRA-1", "kim"), (1, "PDF", null, "kim"));
        (await QueryAsync<string>("SELECT label_type || ' ' || output_kind || ' ' || fields::text FROM bc_label_format ORDER BY label_type", null))
            .Should().Equal(
                """ITEM ZPL ["itemCd", "itemName"]""",
                """LOCATION ZPL ["warehouseCode", "locationCode"]""",
                """LOT ZPL ["itemCd", "lotNo", "expiryDt"]""",
                """PALLET ZPL ["palletNo", "itemCd", "itemName", "lotNo", "qty", "expiryDt"]""");
    }

    private static LotSearchQuery Query(long itemId, long warehouseId, DateOnly date) =>
        new() { ItemId = itemId, WarehouseId = warehouseId, Date = date };

    private static long Lot(IReadOnlyDictionary<string, long> lots, string lotNo) => lots[lotNo];

    [DatabaseFact, Trait("Id", "MM-UT-834"), Trait("Rule", "SD-BR-517")]
    public async Task ShowComponents_IsStoredAndRead_ANewItemHasItOn_AMissingValueKeepsIt()
    {
        var kit = await CreateAsync("KIT");
        var off = await Items.UpdateAsync(kit.Id, Update(kit, lotManaged: false) with { ShowComponents = false }, "kim", None);
        var renamed = await Items.UpdateAsync(kit.Id, Update(off, lotManaged: false) with { ItemName = "KIT 2" }, "kim", None);

        kit.ShowComponents.Should().BeTrue();
        off.ShowComponents.Should().BeFalse();
        renamed.ShowComponents.Should().BeFalse();
        (await QueryAsync<bool>("SELECT show_components FROM bc_item WHERE id = @Id", new { kit.Id })).Single().Should().BeFalse();
    }

    private async Task<ItemDto> CreateAsync(string type = "INVENTORY", bool lotManaged = false, bool locationManaged = false) =>
        await Items.CreateAsync(
            new CreateItemRequest
            {
                ItemCd = "LT" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                ItemName = "LOT TEST",
                ItemType = type,
                ItemClass = "TEST",
                LotManaged = lotManaged,
                LocationManaged = locationManaged,
            },
            "kim",
            None);

    private static UpdateItemRequest Update(ItemDto i, bool lotManaged, bool? locationManaged = null) => new()
    {
        ItemName = i.ItemName,
        ItemType = i.ItemType,
        Unit = i.Unit,
        ItemClass = i.ItemClass,
        ValidFlag = i.ValidFlag,
        UsdPrc = i.UsdPrc,
        VatKind = i.VatKind,
        LotManaged = lotManaged,
        LocationManaged = locationManaged,
        UpdatedAt = i.UpdatedAt,
    };

    private async Task<IReadOnlyList<GoodsMovement>> PostAsync(params StockMovementRequest[] requests)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var posted = await Inventory().PostAsync(requests, "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
        return posted;
    }

    private async Task ReverseAsync(long movementId)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await Inventory().ReverseAsync([movementId], Day, "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
    }

    private InventoryService Inventory() => new(
        db.Get<IInventoryRepository>(),
        db.Get<IInventorySettingRepository>(),
        db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    private async Task<IReadOnlyList<OnHandMismatchRow>> MismatchesAsync(long itemId)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var rows = await db.Get<ILotRepository>().FindOnHandMismatchesAsync(uow.Transaction, None);
        return rows.Where(r => r.ItemId == itemId).ToList();
    }

    // Lot balances of the item, all warehouses together, in lot number order
    private async Task<IReadOnlyList<(string LotNo, decimal Qty)>> LotOnHandAsync(long itemId) =>
        await QueryAsync<(string, decimal)>(
            """
            SELECT l.lot_no, sum(o.onhand_qty) FROM mm_onhand o JOIN mm_lot l ON l.id = o.lot_id
            WHERE o.item_id = @itemId GROUP BY l.lot_no ORDER BY l.lot_no
            """, new { itemId });

    // Key balances of the item, all warehouses together, in Location and lot order
    private async Task<IReadOnlyList<(string LocationCode, string LotNo, decimal Qty)>> KeyOnHandAsync(long itemId) =>
        await QueryAsync<(string, string, decimal)>(
            """
            SELECT c.location_code, l.lot_no, sum(o.onhand_qty) FROM mm_onhand o
            JOIN mm_lot l ON l.id = o.lot_id JOIN bc_location c ON c.id = o.location_id
            WHERE o.item_id = @itemId GROUP BY c.location_code, l.lot_no ORDER BY 1, 2
            """, new { itemId });

    // MAIN is shared by every test, so the code gets a unique suffix: prefix-XXXXXX
    private async Task<(long Id, string Code)> AddLocationAsync(long warehouseId, string prefix)
    {
        var code = prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var id = (await QueryAsync<long>(
            """
            INSERT INTO bc_location (warehouse_id, location_code, created_by, updated_by) VALUES (@warehouseId, @code, 't', 't')
            RETURNING id
            """, new { warehouseId, code })).Single();
        return (id, code);
    }

    private async Task<long> StarLocationAsync(long warehouseId) =>
        (await QueryAsync<long>("SELECT id FROM bc_location WHERE warehouse_id = @warehouseId AND location_code = '*'", new { warehouseId })).Single();

    private async Task<IReadOnlyDictionary<string, long>> LotsAsync(long itemId) =>
        (await QueryAsync<(string LotNo, long Id)>("SELECT lot_no, id FROM mm_lot WHERE item_id = @itemId ORDER BY lot_no", new { itemId }))
        .ToDictionary(l => l.LotNo, l => l.Id);

    // MAIN from the migration and a new second warehouse in the default business place
    private async Task<(long Main, long Second)> WarehousesAsync()
    {
        var main = (await QueryAsync<long>("SELECT id FROM bc_warehouse WHERE is_default", null)).Single();
        var second = (await QueryAsync<long>(
            """
            INSERT INTO bc_warehouse (warehouse_code, warehouse_name, business_place_id, created_by, updated_by)
            SELECT @Code, 'Second', id, 't', 't' FROM bc_business_place WHERE is_default
            RETURNING id
            """, new { Code = "W" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() })).Single();
        return (main, second);
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return (await connection.QueryAsync<T>(sql, args)).ToList();
    }

    private async Task ExecuteAsync(string sql, object args)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.ExecuteAsync(sql, args);
    }

    private sealed class AnyCode : ICodeValidator
    {
        public Task EnsureValidAsync(string majorCode, string? value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedClock : IBusinessClock
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public DateOnly Today() => LotServiceTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}
