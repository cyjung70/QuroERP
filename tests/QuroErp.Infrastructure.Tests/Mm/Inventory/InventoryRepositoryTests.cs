using System.Data;
using Dapper;
using FluentAssertions;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Common.Persistence;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Mm.Inventory;

/// <summary>
/// Warehouse stock after 0213_mm_warehouse_stock.sql and InventoryService on the Dapper repositories, real PostgreSQL
/// (inventory.md, MM-BR-601~603, MM-BR-702). Tests roll back unless they need two transactions.
/// </summary>
[Collection(AuthSchemaDefinition.Name)]
public class InventoryRepositoryTests(AuthSchemaFixture db)
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    [DatabaseFact, Trait("Id", "MM-UT-702")]
    public async Task Migration_CreatesExactlyOneDefaultWarehouse()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var defaults = await uow.Transaction.Connection!.QueryAsync<string>(
            "SELECT warehouse_code FROM bc_warehouse WHERE is_default", transaction: uow.Transaction);

        defaults.Should().Equal("MAIN");
    }

    [DatabaseFact, Trait("Id", "MM-INV-IT-01")]
    public async Task PostAndReverse_UpdateOnHandItemAndLedgerInOneTransaction()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var tx = uow.Transaction;
        var warehouseId = await DefaultWarehouseAsync(tx);
        var itemId = await InsertItemAsync(tx, onHand: 0, cost: 0);

        var receipt = (await Service().PostAsync(
            [new StockMovementRequest("GR", Day, itemId, warehouseId, 4, Price: 2.5m, RefKey: "GR-1", RefSeq: 1)],
            "kim", tx, CancellationToken.None)).Single();
        var issue = (await Service().PostAsync(
            [new StockMovementRequest("DN", Day, itemId, warehouseId, -1, RefKey: "DN-1", RefSeq: 1)],
            "kim", tx, CancellationToken.None)).Single();
        var reversal = (await Service().ReverseAsync([issue.Id], Day.AddDays(1), "kim", tx, CancellationToken.None)).Single();

        var rows = (await tx.Connection!.QueryAsync<(string MovType, decimal Qty, decimal Amount, long? ParentMovementId, string DocumentNo)>(
            """
            SELECT mov_type, qty, amount, parent_movement_id, document_no FROM mm_goods_movement
            WHERE item_id = @itemId ORDER BY id
            """, new { itemId }, tx)).ToList();
        var onHand = await tx.Connection!.QuerySingleAsync<decimal>(
            "SELECT onhand_qty FROM mm_onhand WHERE item_id = @itemId AND warehouse_id = @warehouseId", new { itemId, warehouseId }, tx);
        var item = await tx.Connection!.QuerySingleAsync<(decimal OnhandQty, decimal StkVal, decimal MvgCost)>(
            "SELECT onhand_qty, stk_val, mvg_cost FROM bc_item WHERE id = @itemId", new { itemId }, tx);

        rows.Select(r => (r.MovType, r.Qty, r.Amount, r.ParentMovementId)).Should().Equal(
            ("GR", 4m, 10m, null), ("DN", -1m, -2.5m, null), ("DN", 1m, 2.5m, (long?)issue.Id));
        rows.Select(r => r.DocumentNo[..6]).Should().Equal("ID2610", "ID2610", "ID2610");
        rows.Should().OnlyContain(r => r.DocumentNo.Length == "ID".Length + 4 + 10);
        rows.Select(r => r.DocumentNo).Should().OnlyHaveUniqueItems();
        rows[2].DocumentNo.Should().Be(reversal.DocumentNo);
        onHand.Should().Be(4);
        item.Should().Be((4m, 10m, 2.5m));
        receipt.NegativeStock.Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "MM-UT-606")]
    public async Task ReverseAsync_MovementAlreadyReversedInDatabase_ThrowsMmE606()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var tx = uow.Transaction;
        var warehouseId = await DefaultWarehouseAsync(tx);
        var itemId = await InsertItemAsync(tx, onHand: 0, cost: 0);
        var receipt = (await Service().PostAsync([new StockMovementRequest("GR", Day, itemId, warehouseId, 4, Price: 1)], "kim", tx, CancellationToken.None)).Single();
        await Service().ReverseAsync([receipt.Id], Day, "kim", tx, CancellationToken.None);

        var act = () => Service().ReverseAsync([receipt.Id], Day, "kim", tx, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-606");
    }

    [DatabaseFact, Trait("Id", "MM-UT-603")]
    public async Task PostAsync_TwoConcurrentIssuesInDeny_SecondWaitsForTheLockAndIsBlocked()
    {
        var units = db.Get<IUnitOfWorkFactory>();
        long itemId, warehouseId;
        await using (var setup = await units.BeginAsync(CancellationToken.None))
        {
            warehouseId = await DefaultWarehouseAsync(setup.Transaction);
            itemId = await InsertItemAsync(setup.Transaction, onHand: 0, cost: 0);
            await Service().PostAsync(
                [new StockMovementRequest("GR", Day, itemId, warehouseId, 5, Price: 1)], "setup", setup.Transaction, CancellationToken.None);
            await setup.CommitAsync(CancellationToken.None);
        }
        var issue = new StockMovementRequest("DN", Day, itemId, warehouseId, -3);

        await using var first = await units.BeginAsync(CancellationToken.None);
        await Service().PostAsync([issue], "first", first.Transaction, CancellationToken.None);
        var second = Task.Run(async () =>
        {
            await using var uow = await units.BeginAsync(CancellationToken.None);
            await Service().PostAsync([issue], "second", uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        });
        await Task.Delay(500);
        second.IsCompleted.Should().BeFalse("the second issue waits on the mm_onhand row lock");
        await first.CommitAsync(CancellationToken.None);

        var error = (await second.Invoking(t => t).Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-001");
        error.Args.Should().Equal($"IT-{itemId}", "MAIN", 2m, 3m);
    }

    [DatabaseFact, Trait("Id", "MM-UT-604")]
    public async Task GetDailyBalancesAsync_ClosedMonthOpeningPlusDailySumsWithMonthEnds()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var tx = uow.Transaction;
        var warehouseId = await DefaultWarehouseAsync(tx);
        var itemId = await InsertItemAsync(tx, onHand: 0, cost: 0);
        await tx.Connection!.ExecuteAsync(
            """
            INSERT INTO mm_monthly_inventory (year_month, item_id, warehouse_id, bal_qty, created_by, updated_by)
            VALUES ('202609', @itemId, @warehouseId, 3, 't', 't')
            """, new { itemId, warehouseId }, tx);
        foreach (var (date, qty) in new[] { ("2026-09-15", 100m), ("2026-10-01", 5m), ("2026-10-01", -1m), ("2026-10-20", -2m), ("2026-11-03", 4m) })
        {
            await InsertMovementAsync(tx, itemId, warehouseId, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), qty);
        }

        var (opening, balances) = await db.Get<IInventoryRepository>().GetDailyBalancesAsync(
            itemId, warehouseId, new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 10), tx, CancellationToken.None);

        // 9/15 is in the closed month and is already in the 202609 closing balance
        opening.Should().Be(3);
        balances.Should().Equal(
            new DailyStockBalance(new DateOnly(2026, 10, 1), 7, false),
            new DailyStockBalance(new DateOnly(2026, 10, 20), 5, true),
            new DailyStockBalance(new DateOnly(2026, 11, 3), 9, true));
    }

    [DatabaseFact, Trait("Id", "MM-UT-604")]
    public async Task GetDailyBalancesAsync_NoClosedMonthAndNoMovements_ReturnsZeroOpeningOnly()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var tx = uow.Transaction;
        var itemId = await InsertItemAsync(tx, onHand: 0, cost: 0);

        var (opening, balances) = await db.Get<IInventoryRepository>().GetDailyBalancesAsync(
            itemId, await DefaultWarehouseAsync(tx), null, new DateOnly(2026, 10, 10), tx, CancellationToken.None);

        opening.Should().Be(0);
        balances.Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "MM-UT-604")]
    public async Task Migration_HasTheItemWarehouseDateIndexForBalances()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var definition = await uow.Transaction.Connection!.QuerySingleAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = current_schema() AND indexname = 'ix_mm_goods_movement_item_id_warehouse_id_document_dt'",
            transaction: uow.Transaction);

        definition.Should().EndWith("(item_id, warehouse_id, document_dt)");
    }

    [DatabaseFact, Trait("Id", "MM-UT-009")]
    public async Task PostAsync_DenyBackDatedIssueBeforeTheReceipt_ThrowsMmE009()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var tx = uow.Transaction;
        var warehouseId = await DefaultWarehouseAsync(tx);
        var itemId = await InsertItemAsync(tx, onHand: 0, cost: 0);
        await Service().PostAsync([new StockMovementRequest("GR", Day, itemId, warehouseId, 5, Price: 1)], "kim", tx, CancellationToken.None);

        var act = () => Service().PostAsync(
            [new StockMovementRequest("DN", Day.AddDays(-1), itemId, warehouseId, -2)], "kim", tx, CancellationToken.None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-009");
        error.Args.Should().Equal($"IT-{itemId}", "MAIN", "2026-10-09", -2m);
    }

    private static Task<int> InsertMovementAsync(IDbTransaction tx, long itemId, long warehouseId, DateOnly date, decimal qty) =>
        tx.Connection!.ExecuteAsync(
            """
            INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, qty, prc, amount, created_by, updated_by)
            VALUES (gen_random_uuid()::text, @date, CASE WHEN @qty > 0 THEN 'UR' ELSE 'GI' END, @itemId, @warehouseId, @qty, 1, @qty, 't', 't')
            """, new { itemId, warehouseId, date, qty }, tx);

    private InventoryService Service() => new(
        db.Get<IInventoryRepository>(),
        db.Get<IInventorySettingRepository>(),
        new NoSetComponents(),
        new DocumentNumberService(db.Get<IDocumentNumberRepository>()));

    private static Task<long> DefaultWarehouseAsync(IDbTransaction tx) =>
        tx.Connection!.QuerySingleAsync<long>("SELECT id FROM bc_warehouse WHERE is_default", transaction: tx);

    // item_cd is IT-<id> so concurrent tests never collide
    private static async Task<long> InsertItemAsync(IDbTransaction tx, decimal onHand, decimal cost)
    {
        var id = await tx.Connection!.QuerySingleAsync<long>(
            """
            INSERT INTO bc_item (item_cd, type, onhand_qty, stk_val, mvg_cost, created_by, updated_by)
            VALUES (gen_random_uuid()::text, 'INVENTORY', @onHand, @value, @cost, 't', 't')
            RETURNING id
            """, new { onHand, value = onHand * cost, cost }, tx);
        await tx.Connection!.ExecuteAsync("UPDATE bc_item SET item_cd = 'IT-' || id WHERE id = @id", new { id }, tx);
        return id;
    }

    private sealed class NoSetComponents : ISetComponentRepository
    {
        public Task<IReadOnlyList<SetComponent>> GetComponentsAsync(IReadOnlyCollection<long> setItemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetComponent>>([]);
    }
}
