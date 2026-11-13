using Dapper;
using FluentAssertions;
using Npgsql;
using QuroErp.Application.Bc.Items;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Application.Mm.Lots;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Contracts.Mm.Lots;
using QuroErp.Domain.Common;
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
/// 0404_mm_lot and 0418 (lot balances on mm_onhand), item lot management, lot postings and lot search on real PostgreSQL
/// (TASK-CORE-027, TASK-CORE-067, MM-BR-351~356, 361~363).
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

    private LotService Lots => new(db.Get<ILotRepository>(), db.Get<IUnitOfWorkFactory>());

    [DatabaseFact, Trait("Id", "MM-UT-351"), Trait("Rule", "MM-BR-351")]
    public async Task TurningOn_KeepsEachWarehouseStockOnLotStar_AndStartsToday()
    {
        var (main, second) = await WarehousesAsync();
        var item = await CreateAsync();
        await PostAsync(new("GR", Before, item.Id, main, 10, Price: 2), new("GR", Before, item.Id, second, 4, Price: 2));

        var saved = await Items.UpdateAsync(item.Id, Update(await Items.GetAsync(item.Id, None), lotManaged: true), "kim", None);

        (saved.LotManaged, saved.LotManagedFrom).Should().Be((true, Today));
        (await Lots.SearchAsync(Query(item.Id, main, Today), None)).Should().Equal(new LotBalanceDto(Lot(await LotsAsync(item.Id), "*"), "*", 10));
        (await Lots.SearchAsync(Query(item.Id, second, Today), None)).Single().OnHandQty.Should().Be(4);
        (await MismatchesAsync(item.Id)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "MM-UT-351"), Trait("Id", "MM-UT-361"), Trait("Rule", "MM-BR-361")]
    public async Task TurningOn_IsRefused_ForANegativeWarehouse_ASetComponent_OrAServiceItem()
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
        var set = await CreateAsync(type: "SET");
        await ExecuteAsync(
            "INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by) VALUES (@Set, 1, @Component, 1, 't', 't')",
            new { Set = set.Id, Component = component.Id });

        var onNegative = () => Items.UpdateAsync(negative.Id, Update(negative, lotManaged: true), "kim", None);
        var onComponent = () => Items.UpdateAsync(component.Id, Update(component, lotManaged: true), "kim", None);
        var onService = () => CreateAsync(type: "SERVICE", lotManaged: true);

        (await onNegative.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.ManagementCode);
        (await onComponent.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.SetComponentCode);
        (await onService.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(LotRules.ManagementCode);
        (await Items.GetAsync(component.Id, None)).LotManaged.Should().BeFalse();
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
        (error.Code, error.Args[3], error.Args[4]).Should().Be((LotRules.NegativeCode, (object)"2026-10-09", (object)(-1m)));
        (await LotOnHandAsync(item.Id)).Should().Equal(("*", 0m), ("A", 5m), ("B1", 5m));
    }

    [DatabaseFact, Trait("Id", "MM-UT-362"), Trait("Rule", "MM-BR-362")]
    public async Task Search_ShowsLotsAboveZeroAtTheEndOfTheDate_InLotNumberOrder()
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

        onDay.Select(l => (l.LotNo, l.OnHandQty)).Should().Equal(("A", 2m), ("C", 5m));
        later.Select(l => (l.LotNo, l.OnHandQty)).Should().Equal(("B", 4m), ("C", 5m));
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

    private static LotSearchQuery Query(long itemId, long warehouseId, DateOnly date) =>
        new() { ItemId = itemId, WarehouseId = warehouseId, Date = date };

    private static long Lot(IReadOnlyDictionary<string, long> lots, string lotNo) => lots[lotNo];

    private async Task<ItemDto> CreateAsync(string type = "INVENTORY", bool lotManaged = false) =>
        await Items.CreateAsync(
            new CreateItemRequest
            {
                ItemCd = "LT" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                ItemName = "LOT TEST",
                ItemType = type,
                ItemClass = "TEST",
                LotManaged = lotManaged,
            },
            "kim",
            None);

    private static UpdateItemRequest Update(ItemDto i, bool lotManaged) => new()
    {
        ItemName = i.ItemName,
        ItemType = i.ItemType,
        Unit = i.Unit,
        ItemClass = i.ItemClass,
        ValidFlag = i.ValidFlag,
        UsdPrc = i.UsdPrc,
        VatKind = i.VatKind,
        LotManaged = lotManaged,
        UpdatedAt = i.UpdatedAt,
    };

    private async Task<IReadOnlyList<QuroErp.Domain.Mm.Inventory.GoodsMovement>> PostAsync(params StockMovementRequest[] requests)
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

    private async Task<IReadOnlyList<LotMismatchRow>> MismatchesAsync(long itemId)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var rows = await db.Get<ILotRepository>().FindLotMismatchesAsync(uow.Transaction, None);
        return rows.Where(r => r.ItemId == itemId).ToList();
    }

    // Lot balances of the item, all warehouses together, in lot number order
    private async Task<IReadOnlyList<(string LotNo, decimal Qty)>> LotOnHandAsync(long itemId) =>
        await QueryAsync<(string, decimal)>(
            """
            SELECT l.lot_no, sum(o.onhand_qty) FROM mm_onhand o JOIN mm_lot l ON l.id = o.lot_id
            WHERE o.item_id = @itemId GROUP BY l.lot_no ORDER BY l.lot_no
            """, new { itemId });

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
