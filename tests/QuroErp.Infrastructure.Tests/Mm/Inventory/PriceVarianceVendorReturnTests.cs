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
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Inventory;

/// <summary>Own schema: the inventory service commits and these tests close a month, so nothing may reach other test classes.</summary>
[CollectionDefinition(Name)]
public sealed class PriceVarianceSchemaDefinition : ICollectionFixture<AuthSchemaFixture>
{
    public const string Name = "PriceVarianceSchema";
}

/// <summary>
/// Movement types PV (supplier invoice price difference) and VR (vendor return) on real PostgreSQL (TASK-CORE-028, MM-BR-005,
/// MM-BR-601, MM-BR-602, MM-BR-606, MM-BR-759, MM-BR-785). Each test makes its own item in the MAIN warehouse; the negative
/// stock mode is DENY.
/// </summary>
[Collection(PriceVarianceSchemaDefinition.Name)]
public class PriceVarianceVendorReturnTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly DateOnly Day = new(2026, 10, 10);

    private ItemService Items => new(
        db.Get<IItemRepository>(), db.Get<ILotRepository>(), new AnyCode(), db.Get<IUnitOfWorkFactory>(), new FixedClock());

    [DatabaseFact, Trait("Id", "MM-UT-759"), Trait("Rule", "MM-BR-759"), Trait("Rule", "MM-BR-005")]
    public async Task PriceVariance_ChangesTheValueAndCost_NotTheStock_AndHasNoLot()
    {
        var main = await MainAsync();
        var item = await CreateAsync(lotManaged: true);
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 10, Price: 15, LotNo: "A"));

        var posted = await PostAsync(new StockMovementRequest("PV", Day, item.Id, main, 0, RefKey: "SI-1", RefSeq: 1, Amount: 20));

        (await StockAsync(item.Id)).Should().Be((10m, 170m, 17m));
        (await OnHandAsync(item.Id, main)).Should().Be(10m);
        (await QueryAsync<(decimal Qty, decimal Prc, decimal Amount, long? LotId)>(
            "SELECT qty, prc, amount, lot_id FROM mm_goods_movement WHERE id = @Id", new { posted.Single().Id }))
            .Single().Should().Be((0m, 0m, 20m, (long?)null));
        (await QueryAsync<long>("SELECT count(*) FROM mm_lot WHERE item_id = @Id", new { item.Id })).Single().Should().Be(1, "only lot A");
    }

    [DatabaseFact, Trait("Id", "MM-UT-005"), Trait("Rule", "MM-BR-005")]
    public async Task PriceVariance_WithoutStock_KeepsTheCost()
    {
        var main = await MainAsync();
        var item = await CreateAsync();
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 4, Price: 10));
        await PostAsync(new StockMovementRequest("DN", Day, item.Id, main, -4));

        await PostAsync(new StockMovementRequest("PV", Day, item.Id, main, 0, Amount: 8));

        (await StockAsync(item.Id)).Should().Be((0m, 8m, 10m));
    }

    [DatabaseFact, Trait("Id", "MM-UT-759"), Trait("Rule", "MM-BR-759"), Trait("Rule", "MM-BR-601")]
    public async Task PriceVarianceInAClosedMonth_PostsOnTheFirstOpenDay_AndSoDoesItsReversal()
    {
        var main = await MainAsync();
        var item = await CreateAsync();
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 10));
        // The test schema has no company row until one is set up; this class owns its schema
        await ExecuteAsync("""
            INSERT INTO bc_company (name, country_code, base_currency_code, default_lang_code, time_zone, created_by, updated_by)
            SELECT 'Quro', 'KR', 'KRW', 'ko', 'Asia/Seoul', 't', 't' WHERE NOT EXISTS (SELECT 1 FROM bc_company);
            UPDATE bc_company SET close_year_month = '202601';
            """, null);
        try
        {
            var posted = await PostAsync(new StockMovementRequest("PV", new DateOnly(2025, 12, 20), item.Id, main, 0, Amount: 10));
            (await StockAsync(item.Id)).Should().Be((5m, 60m, 12m));

            var reversed = await ReverseAsync(posted.Single().Id, new DateOnly(2025, 12, 25));

            (posted.Single().DocumentDt, reversed.Single().DocumentDt).Should().Be((new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
            (reversed.Single().Qty, reversed.Single().Amount).Should().Be((0m, -10m));
            (await StockAsync(item.Id)).Should().Be((5m, 50m, 10m));
        }
        finally
        {
            await ExecuteAsync("UPDATE bc_company SET close_year_month = NULL", null);
        }
    }

    [DatabaseFact, Trait("Id", "MM-UT-785"), Trait("Rule", "MM-BR-785"), Trait("Rule", "MM-BR-601")]
    public async Task VendorReturn_LeavesAtTheAskedPrice_AndRecalculatesTheCost_AndItsReversalPutsItBack()
    {
        var main = await MainAsync();
        var item = await CreateAsync();
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 10, Price: 15));

        var posted = await PostAsync(new StockMovementRequest("VR", Day, item.Id, main, -4, Price: 12, RefKey: "VR-1", RefSeq: 1));

        (posted.Single().Prc, posted.Single().Amount).Should().Be((12m, -48m));
        (await StockAsync(item.Id)).Should().Be((6m, 102m, 17m));
        (await OnHandAsync(item.Id, main)).Should().Be(6m);

        await ReverseAsync(posted.Single().Id, Day);

        (await StockAsync(item.Id)).Should().Be((10m, 150m, 15m));
    }

    [DatabaseFact, Trait("Id", "MM-UT-602"), Trait("Rule", "MM-BR-602"), Trait("Rule", "MM-BR-001")]
    public async Task VendorReturn_AboveTheStock_IsMmE001_AndSavesNothing()
    {
        var main = await MainAsync();
        var item = await CreateAsync();
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 2, Price: 10));

        var act = () => PostAsync(new StockMovementRequest("VR", Day, item.Id, main, -3, Price: 10));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(StockPolicy.InsufficientStock);
        (await StockAsync(item.Id)).Should().Be((2m, 20m, 10m));
        (await QueryAsync<long>("SELECT count(*) FROM mm_goods_movement WHERE item_id = @Id AND mov_type = 'VR'", new { item.Id }))
            .Single().Should().Be(0);
    }

    [DatabaseFact, Trait("Id", "MM-UT-356"), Trait("Rule", "MM-BR-356")]
    public async Task VendorReturn_AboveTheLotBalance_IsMmE356()
    {
        var main = await MainAsync();
        var item = await CreateAsync(lotManaged: true);
        // Lot B keeps the warehouse stock above the return, so only the lot check stops it
        await PostAsync(
            new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2, LotNo: "A"),
            new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 2, LotNo: "B"));

        var act = () => PostAsync(new StockMovementRequest("VR", Day, item.Id, main, -6, Price: 2, LotNo: "A"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-356");
    }

    [DatabaseFact, Trait("Id", "MM-UT-606"), Trait("Rule", "MM-BR-606")]
    public async Task PriceVarianceWithQty_IsMmE606_AndAVendorReturnWithoutPrice_IsRefused()
    {
        var main = await MainAsync();
        var item = await CreateAsync();
        await PostAsync(new StockMovementRequest("GR", Day, item.Id, main, 5, Price: 10));

        var withQty = () => PostAsync(new StockMovementRequest("PV", Day, item.Id, main, 1, Amount: 5));
        var withoutPrice = () => PostAsync(new StockMovementRequest("VR", Day, item.Id, main, -1));

        (await withQty.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(GoodsMovement.InvalidMovement);
        await withoutPrice.Should().ThrowAsync<ArgumentException>();
        (await StockAsync(item.Id)).Should().Be((5m, 50m, 10m));
    }

    private async Task<ItemDto> CreateAsync(bool lotManaged = false) =>
        await Items.CreateAsync(
            new CreateItemRequest
            {
                ItemCd = "PV" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                ItemName = "PV VR TEST",
                ItemType = "INVENTORY",
                ItemClass = "TEST",
                LotManaged = lotManaged,
            },
            "kim",
            None);

    private async Task<IReadOnlyList<GoodsMovement>> PostAsync(params StockMovementRequest[] requests)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var posted = await Inventory().PostAsync(requests, "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
        return posted;
    }

    private async Task<IReadOnlyList<GoodsMovement>> ReverseAsync(long movementId, DateOnly reversalDt)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var reversed = await Inventory().ReverseAsync([movementId], reversalDt, "kim", uow.Transaction, None);
        await uow.CommitAsync(None);
        return reversed;
    }

    private InventoryService Inventory() => new(
        db.Get<IInventoryRepository>(),
        db.Get<IInventorySettingRepository>(),
        db.Get<ISetComponentRepository>(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    // MAIN from the migration, with the DENY mode these tests expect
    private async Task<long> MainAsync()
    {
        await ExecuteAsync("UPDATE mm_inventory_setting SET negative_stock_mode = 'DENY'", null);
        return (await QueryAsync<long>("SELECT id FROM bc_warehouse WHERE is_default", null)).Single();
    }

    private async Task<(decimal OnHandQty, decimal StockValue, decimal MovingAverageCost)> StockAsync(long itemId) =>
        (await QueryAsync<(decimal, decimal, decimal)>("SELECT onhand_qty, stk_val, mvg_cost FROM bc_item WHERE id = @itemId", new { itemId })).Single();

    private async Task<decimal> OnHandAsync(long itemId, long warehouseId) =>
        (await QueryAsync<decimal>(
            "SELECT onhand_qty FROM mm_onhand WHERE item_id = @itemId AND warehouse_id = @warehouseId", new { itemId, warehouseId })).Single();

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return (await connection.QueryAsync<T>(sql, args)).ToList();
    }

    private async Task ExecuteAsync(string sql, object? args)
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

        public DateOnly Today() => PriceVarianceVendorReturnTests.Today;

        public DateTimeOffset ToCompanyTime(DateTimeOffset at) => at;

        public void Reset()
        {
        }
    }
}
