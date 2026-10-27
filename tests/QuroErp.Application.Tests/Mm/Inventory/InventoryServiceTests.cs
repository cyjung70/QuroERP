using System.Data;
using FluentAssertions;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Tests.Mm.Inventory;

/// <summary>
/// InventoryService with in-memory repositories: on-hand per item + warehouse, ledger, cost and sets
/// (MM-BR-001~008, MM-BR-601~603, MM-BR-606).
/// </summary>
public class InventoryServiceTests
{
    private const long Main = 1;
    private const long Second = 2;
    private static readonly DateOnly Day = new(2026, 10, 10);

    private readonly FakeInventoryRepository _inventory = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeSetComponents _sets = new();
    private readonly FakeDocumentNumbers _numbers = new();

    private InventoryService Service => new(_inventory, _settings, _sets,
        new DocumentNumberService(_numbers));

    private Task<IReadOnlyList<GoodsMovement>> Post(params StockMovementRequest[] requests) =>
        Service.PostAsync(requests, "kim", null!, CancellationToken.None);

    [Fact, Trait("Id", "MM-UT-001")]
    public async Task PostAsync_DenyIssueAboveWarehouseOnHand_ThrowsAndSavesNothing()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);
        _inventory.AddOnHand(10, Second, 100);

        var act = () => Post(new StockMovementRequest("DN", Day, 10, Main, -6));

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be("MM-E-001");
        error.Args.Should().Equal("A", "WH1", 5m, 6m);
        _inventory.Movements.Should().BeEmpty();
        _inventory.OnHand[(10, Main)].Should().Be(5);
    }

    [Theory, Trait("Id", "MM-UT-002")]
    [InlineData("ALLOW")]
    [InlineData("ALLOW_IN_MONTH")]
    public async Task PostAsync_AllowModesIssueAboveOnHand_SavesNegativeAndFlagsMovement(string mode)
    {
        _settings.Mode = mode;
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);

        var posted = await Post(new StockMovementRequest("DN", Day, 10, Main, -6));

        posted.Should().ContainSingle().Which.NegativeStock.Should().BeTrue();
        _inventory.OnHand[(10, Main)].Should().Be(-1);
        _inventory.Items[10].OnHandQty.Should().Be(-1);
    }

    [Fact, Trait("Id", "MM-UT-003")]
    public async Task PostAsync_NoSettingRow_IsDeny()
    {
        _settings.Mode = null;
        _inventory.AddItem(10, "A", onHand: (Main, 1m), cost: 2);

        var act = () => Post(new StockMovementRequest("GI", Day, 10, Main, -2));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-001");
    }

    [Fact, Trait("Id", "MM-UT-004")]
    public async Task PostAsync_DateInClosedMonth_ThrowsMmE002()
    {
        _inventory.CloseYearMonth = "202610";
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 2);

        var act = () => Post(new StockMovementRequest("GR", new DateOnly(2026, 9, 30), 10, Main, 1, Price: 3));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-002");
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public async Task PostAsync_ReceiptThenIssue_IssueUsesTheNewMovingAverageCost()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 10m), cost: 10);

        var posted = await Post(
            new StockMovementRequest("GR", Day, 10, Main, 10, Price: 20, RefKey: "GR-1", RefSeq: 1),
            new StockMovementRequest("DN", Day, 10, Main, -4, RefKey: "DN-1", RefSeq: 1));

        posted.Select(m => (m.Prc, m.Amount)).Should().Equal((20m, 200m), (15m, -60m));
        var item = _inventory.Items[10];
        (item.OnHandQty, item.StockValue, item.MovingAverageCost).Should().Be((16m, 240m, 15m));
    }

    [Fact, Trait("Id", "MM-UT-007")]
    public async Task PostAsync_SetIssue_ReducesEachComponentByQtyTimesComponentQty()
    {
        _inventory.AddItem(90, "SET", type: "SET");
        _inventory.AddItem(1, "P1", onHand: (Main, 20m), cost: 3);
        _inventory.AddItem(2, "P2", onHand: (Main, 20m), cost: 5);
        _sets.Components.AddRange([new SetComponent(90, 1, 2), new SetComponent(90, 2, 1)]);

        var posted = await Post(new StockMovementRequest("DN", Day, 90, Main, -3, RefKey: "DN-1", RefSeq: 1));

        posted.Select(m => (m.ItemId, m.Qty, m.Amount)).Should().Equal((1L, -6m, -18m), (2L, -3m, -15m));
        _inventory.OnHand[(1, Main)].Should().Be(14);
        _inventory.OnHand[(2, Main)].Should().Be(17);
        _inventory.OnHand.Should().NotContainKey((90, Main), "a set keeps no stock");
    }

    [Fact, Trait("Id", "MM-UT-008")]
    public async Task PostAsync_ServiceItem_MakesNoMovement()
    {
        _inventory.AddItem(10, "FEE", type: "SERVICE");

        var posted = await Post(new StockMovementRequest("DN", Day, 10, Main, -1, Price: 50));

        posted.Should().BeEmpty();
        _inventory.Movements.Should().BeEmpty();
    }

    [Fact, Trait("Id", "MM-UT-601")]
    public async Task ReverseAsync_AddsOppositeMovementWithParentAndNewNumber()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5, RefKey: "GR-1", RefSeq: 2))).Single();

        var reversal = (await Service.ReverseAsync([receipt.Id], new DateOnly(2026, 10, 20), "kim", null!, CancellationToken.None)).Single();

        (reversal.MovType, reversal.Qty, reversal.Amount, reversal.ParentMovementId).Should().Be(("GR", -4m, -20m, receipt.Id));
        reversal.DocumentDt.Should().Be(new DateOnly(2026, 10, 20));
        reversal.DocumentNo.Should().Be("GM2026100000000002");
        receipt.DocumentNo.Should().Be("GM2026100000000001");
        _inventory.OnHand[(10, Main)].Should().Be(0);
    }

    [Fact, Trait("Id", "MM-UT-602")]
    public async Task ReverseAsync_ReceiptReversalBelowOnHandInDeny_ThrowsMmE001()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5))).Single();
        await Post(new StockMovementRequest("DN", Day, 10, Main, -3));

        var act = () => Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-001");
    }

    [Fact, Trait("Id", "MM-UT-602")]
    public async Task ReverseAsync_IssueReversalIncreasesStock_IsNotChecked()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 3m), cost: 2);
        var issue = (await Post(new StockMovementRequest("GI", Day, 10, Main, -3))).Single();

        var reversal = (await Service.ReverseAsync([issue.Id], Day, "kim", null!, CancellationToken.None)).Single();

        reversal.Qty.Should().Be(3);
        _inventory.OnHand[(10, Main)].Should().Be(3);
    }

    [Fact, Trait("Id", "MM-UT-603")]
    public async Task PostAsync_LocksOnHandRowsInItemWarehouseOrderBeforeChecking()
    {
        _inventory.AddItem(1, "A", onHand: (Main, 5m), cost: 1);
        _inventory.AddItem(2, "B", onHand: (Main, 5m), cost: 1);
        _inventory.AddOnHand(1, Second, 5);

        await Post(
            new StockMovementRequest("DN", Day, 2, Main, -1),
            new StockMovementRequest("DN", Day, 1, Second, -1),
            new StockMovementRequest("DN", Day, 1, Main, -1));

        _inventory.Locked.Should().Equal((1L, Main), (1L, Second), (2L, Main));
    }

    [Theory, Trait("Id", "MM-UT-606")]
    [InlineData("GR", -1)]
    [InlineData("DN", 1)]
    [InlineData("GI", 0)]
    public async Task PostAsync_QtySignNotAllowedForType_ThrowsMmE606(string type, int qty)
    {
        _inventory.AddItem(10, "A", onHand: (Main, 5m), cost: 1);

        var act = () => Post(new StockMovementRequest(type, Day, 10, Main, qty, Price: 1));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-UT-606")]
    public async Task ReverseAsync_AlreadyReversedMovement_ThrowsMmE606()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5))).Single();
        await Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None);

        var act = () => Service.ReverseAsync([receipt.Id], Day, "kim", null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-UT-606")]
    public async Task ReverseAsync_SameMovementTwiceInOneCall_ThrowsMmE606()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);
        var receipt = (await Post(new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5))).Single();

        var act = () => Service.ReverseAsync([receipt.Id, receipt.Id], Day, "kim", null!, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("MM-E-606");
    }

    [Fact, Trait("Id", "MM-INV-T01")]
    public async Task PostAsync_ItemOnHandIsTheSumOfWarehouses()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);

        await Post(
            new StockMovementRequest("GR", Day, 10, Main, 4, Price: 5),
            new StockMovementRequest("GR", Day, 10, Second, 6, Price: 5),
            new StockMovementRequest("DN", Day, 10, Second, -1));

        _inventory.OnHand[(10, Main)].Should().Be(4);
        _inventory.OnHand[(10, Second)].Should().Be(5);
        _inventory.Items[10].OnHandQty.Should().Be(9);
    }

    [Fact, Trait("Id", "MM-GM-T02")]
    public async Task PostAsync_MovementNumberUsesTheDocumentDateMonthWithOneSequencePerMonth()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 0m), cost: 0);

        var first = await Post(
            new StockMovementRequest("GR", new DateOnly(2026, 10, 10), 10, Main, 1, Price: 1),
            new StockMovementRequest("GR", new DateOnly(2026, 9, 30), 10, Main, 1, Price: 1));
        var second = await Post(
            new StockMovementRequest("GR", new DateOnly(2026, 9, 1), 10, Main, 1, Price: 1),
            new StockMovementRequest("GR", new DateOnly(2026, 10, 31), 10, Main, 1, Price: 1));

        first.Concat(second).Select(m => m.DocumentNo).Should().Equal(
            "GM2026100000000001", "GM2026090000000001", "GM2026090000000002", "GM2026100000000002");
        _numbers.Last.Keys.Should().BeEquivalentTo("GM202609", "GM202610");
    }

    [Fact, Trait("Id", "MM-UT-005")]
    public async Task PostAsync_StockCountIncrease_RecalculatesCostLikeAReceipt()
    {
        _inventory.AddItem(10, "A", onHand: (Main, 10m), cost: 10);

        await Post(new StockMovementRequest("CT", Day, 10, Main, 10, Price: 20));
        var afterIncrease = _inventory.Items[10].MovingAverageCost;
        await Post(new StockMovementRequest("CT", Day, 10, Main, -5, Price: 99));

        afterIncrease.Should().Be(15);
        (_inventory.Items[10].OnHandQty, _inventory.Items[10].MovingAverageCost).Should().Be((15m, 15m));
    }

    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        public string? CloseYearMonth { get; set; }

        public Dictionary<long, ItemStock> Items { get; } = [];

        public Dictionary<(long ItemId, long WarehouseId), decimal> OnHand { get; } = [];

        public List<GoodsMovement> Movements { get; } = [];

        public List<(long ItemId, long WarehouseId)> Locked { get; } = [];

        public void AddItem(long id, string itemCd, (long WarehouseId, decimal Qty)? onHand = null, decimal cost = 0, string type = "INVENTORY")
        {
            var qty = onHand?.Qty ?? 0;
            Items[id] = new ItemStock(id, itemCd, type, qty, qty * cost, cost);
            if (onHand is { } o)
            {
                OnHand[(id, o.WarehouseId)] = o.Qty;
            }
        }

        public void AddOnHand(long itemId, long warehouseId, decimal qty)
        {
            OnHand[(itemId, warehouseId)] = qty;
            var item = Items[itemId];
            Items[itemId] = new ItemStock(itemId, item.ItemCd, item.Type, item.OnHandQty + qty, item.StockValue + qty * item.MovingAverageCost, item.MovingAverageCost);
        }

        public Task<string?> GetCloseYearMonthAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(CloseYearMonth);

        // Copies, so a failed post does not change the stored items
        public Task<IReadOnlyList<ItemStock>> LockItemsAsync(IReadOnlyCollection<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ItemStock>>(itemIds.Where(Items.ContainsKey).Select(id => Items[id])
                .Select(i => new ItemStock(i.ItemId, i.ItemCd, i.Type, i.OnHandQty, i.StockValue, i.MovingAverageCost)).ToList());

        public Task<OnHandRow> LockOnHandAsync(long itemId, long warehouseId, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Locked.Add((itemId, warehouseId));
            return Task.FromResult(new OnHandRow(itemId, warehouseId, $"WH{warehouseId}", OnHand.GetValueOrDefault((itemId, warehouseId))));
        }

        public Task UpdateOnHandAsync(long itemId, long warehouseId, decimal onHandQty, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            OnHand[(itemId, warehouseId)] = onHandQty;
            return Task.CompletedTask;
        }

        public Task UpdateItemStockAsync(ItemStock item, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Items[item.ItemId] = item;
            return Task.CompletedTask;
        }

        public Task InsertMovementAsync(GoodsMovement movement, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            movement.Id = Movements.Count + 1;
            Movements.Add(movement);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SavedMovement>> LockMovementsAsync(IReadOnlyCollection<long> movementIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SavedMovement>>(Movements.Where(m => movementIds.Contains(m.Id))
                .Select(m => new SavedMovement(m, Movements.Any(r => r.ParentMovementId == m.Id))).ToList());
    }

    private sealed class FakeSettings : IInventorySettingRepository
    {
        public string? Mode { get; set; } = "DENY";

        public Task<InventorySettingRow?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Mode is null ? null : new InventorySettingRow(Mode, DateTimeOffset.UnixEpoch, "system"));

        public Task<InventorySettingRow?> UpdateAsync(
            string negativeStockMode, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSetComponents : ISetComponentRepository
    {
        public List<SetComponent> Components { get; } = [];

        public Task<IReadOnlyList<SetComponent>> GetComponentsAsync(IReadOnlyCollection<long> setItemIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetComponent>>(Components.Where(c => setItemIds.Contains(c.SetItemId)).ToList());
    }

    private sealed class FakeDocumentNumbers : IDocumentNumberRepository
    {
        public Dictionary<string, string> Last { get; } = [];

        public Task<string?> LockLastAsync(string transType, string user, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(Last.GetValueOrDefault(transType));

        public Task SaveLastAsync(string transType, string lastNo, string user, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Last[transType] = lastNo;
            return Task.CompletedTask;
        }
    }
}
