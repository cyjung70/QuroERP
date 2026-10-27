using System.Data;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Mm.Inventory;

/// <summary>
/// One stock change asked by a document line. Qty is signed (+ in, − out). Price null means the item's moving
/// average cost at that moment (issues); receipts pass their price.
/// </summary>
public sealed record StockMovementRequest(
    string MovType, DateOnly DocumentDt, long ItemId, long WarehouseId, decimal Qty,
    decimal? Price = null, string? RefKey = null, int? RefSeq = null);

/// <summary>
/// Posts stock movements for every stock document (inventory.md, AS IS receipt_inv, issue_inv, insert_inv, update_inv).
/// Runs in the caller's transaction so the document, its lines and its movements are saved together
/// (AGENTS.md §5.8). Any exception must roll the whole document back (MM-BR-603).
/// </summary>
public sealed class InventoryService(
    IInventoryRepository inventory,
    IInventorySettingRepository settings,
    ISetComponentRepository sets,
    DocumentNumberService numbers)
{
    /// <summary>Movement numbers: GM + yyyyMM of the movement date + 10 digits.</summary>
    public const string MovementNumberPrefix = "GM";

    public const int MovementNumberDigits = 10;

    /// <summary>
    /// Posts original movements. Service items are skipped and set items are expanded into their components
    /// (MM-BR-007, MM-BR-008).
    /// </summary>
    /// <exception cref="QuroErp.Domain.Common.DomainException">
    /// MM-E-002 closed month, MM-E-606 wrong sign or zero qty, MM-E-001 not enough stock in DENY mode.
    /// </exception>
    public async Task<IReadOnlyList<GoodsMovement>> PostAsync(
        IReadOnlyList<StockMovementRequest> requests, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var closeYearMonth = await inventory.GetCloseYearMonthAsync(transaction, cancellationToken);
        foreach (var request in requests)
        {
            InventoryClosing.EnsureOpen(closeYearMonth, request.DocumentDt);
        }

        var items = await LockItemsAsync(requests.Select(r => r.ItemId), transaction, cancellationToken);
        var setIds = items.Values.Where(i => i.IsSet).Select(i => i.ItemId).ToList();
        var components = setIds.Count == 0
            ? []
            : await sets.GetComponentsAsync(setIds, transaction, cancellationToken);
        var componentIds = components.Select(c => c.ComponentItemId).Where(id => !items.ContainsKey(id));
        foreach (var (id, item) in await LockItemsAsync(componentIds, transaction, cancellationToken))
        {
            items[id] = item;
        }

        var lines = new List<Line>();
        foreach (var request in requests)
        {
            var item = items[request.ItemId];
            if (!item.IsSet)
            {
                lines.Add(new Line(request.ItemId, request.WarehouseId, stock => GoodsMovement.Create(
                    request.MovType, request.DocumentDt, request.ItemId, request.WarehouseId, request.Qty,
                    request.Price ?? stock.MovingAverageCost, request.RefKey, request.RefSeq)));
                continue;
            }
            // Components move at their own moving average cost (MM-BR-007)
            foreach (var (componentId, qty) in SetComponent.Expand(request.Qty, components.Where(c => c.SetItemId == item.ItemId)))
            {
                lines.Add(new Line(componentId, request.WarehouseId, stock => GoodsMovement.Create(
                    request.MovType, request.DocumentDt, componentId, request.WarehouseId, qty,
                    stock.MovingAverageCost, request.RefKey, request.RefSeq)));
            }
        }

        var stockLines = lines.Where(l => items[l.ItemId].IsStockManaged).ToList();
        return await ApplyAsync(stockLines, items, user, transaction, cancellationToken);
    }

    /// <summary>
    /// Cancels movements: each gets a reversal dated reversalDt with the same type and price and the opposite sign
    /// (MM-BR-601). A reversal that decreases stock (e.g. a receipt reversal) is checked like an issue (MM-BR-602).
    /// </summary>
    /// <exception cref="QuroErp.Domain.Common.DomainException">
    /// MM-E-002 closed month, MM-E-606 already reversed or a reversal, MM-E-001 not enough stock in DENY mode.
    /// </exception>
    public async Task<IReadOnlyList<GoodsMovement>> ReverseAsync(
        IReadOnlyList<long> movementIds, DateOnly reversalDt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var closeYearMonth = await inventory.GetCloseYearMonthAsync(transaction, cancellationToken);
        InventoryClosing.EnsureOpen(closeYearMonth, reversalDt);

        var saved = (await inventory.LockMovementsAsync(movementIds, transaction, cancellationToken)).ToDictionary(s => s.Movement.Id);
        var reversedNow = new HashSet<long>();
        var reversals = new List<GoodsMovement>();
        foreach (var id in movementIds)
        {
            var original = saved.GetValueOrDefault(id) ?? throw new InvalidOperationException($"Goods movement {id} does not exist.");
            // The same id twice in one call is a second reversal
            reversals.Add(original.Movement.Reverse(reversalDt, original.Reversed || !reversedNow.Add(id)));
        }

        var items = await LockItemsAsync(reversals.Select(r => r.ItemId), transaction, cancellationToken);
        var lines = reversals.Select(r => new Line(r.ItemId, r.WarehouseId, _ => r)).ToList();
        return await ApplyAsync(lines, items, user, transaction, cancellationToken);
    }

    // MM-BR-603: ① lock on-hand ② on-hand check ③ point-in-time check ④ update on-hand, item and ledger
    private async Task<IReadOnlyList<GoodsMovement>> ApplyAsync(
        IReadOnlyList<Line> lines, Dictionary<long, ItemStock> items, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var mode = StockPolicy.ModeOrDefault((await settings.GetAsync(transaction, cancellationToken))?.NegativeStockMode);

        // Lock in a fixed order so two documents touching the same rows cannot deadlock
        var onHand = new Dictionary<(long ItemId, long WarehouseId), OnHandRow>();
        foreach (var key in lines.Select(l => (l.ItemId, l.WarehouseId)).Distinct().Order())
        {
            onHand[key] = await inventory.LockOnHandAsync(key.ItemId, key.WarehouseId, user, transaction, cancellationToken);
        }
        var qty = onHand.ToDictionary(e => e.Key, e => e.Value.OnHandQty);

        var posted = new List<GoodsMovement>(lines.Count);
        foreach (var line in lines)
        {
            var key = (line.ItemId, line.WarehouseId);
            var item = items[line.ItemId];
            var movement = line.Build(item);
            movement.NegativeStock = StockPolicy.CheckOnHand(mode, qty[key], movement.Qty, item.ItemCd, onHand[key].WarehouseCode);
            // ③ Point-in-time and month-end balances (MM-BR-009, MM-BR-010): TASK-CORE-009
            qty[key] += movement.Qty;
            item.Apply(movement.Qty, movement.Amount, movement.Prc, MovementType.RecalculatesCost(movement.MovType, movement.Qty));
            posted.Add(movement);
        }

        var documentNos = await numbers.NextAsync(
            MovementNumberPrefix, MovementNumberDigits, posted.Select(m => m.DocumentDt).ToList(), user, transaction, cancellationToken);
        foreach (var (movement, documentNo) in posted.Zip(documentNos))
        {
            movement.DocumentNo = documentNo;
            await inventory.InsertMovementAsync(movement, user, transaction, cancellationToken);
        }

        foreach (var ((itemId, warehouseId), onHandQty) in qty)
        {
            await inventory.UpdateOnHandAsync(itemId, warehouseId, onHandQty, user, transaction, cancellationToken);
        }
        foreach (var itemId in lines.Select(l => l.ItemId).Distinct())
        {
            await inventory.UpdateItemStockAsync(items[itemId], user, transaction, cancellationToken);
        }
        return posted;
    }

    private async Task<Dictionary<long, ItemStock>> LockItemsAsync(IEnumerable<long> itemIds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        var items = (await inventory.LockItemsAsync(ids, transaction, cancellationToken)).ToDictionary(i => i.ItemId);
        var missing = ids.Where(id => !items.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Items {string.Join(", ", missing)} do not exist.");
        }
        return items;
    }

    // Build runs at posting time so an issue takes the moving average cost after the lines before it
    private sealed record Line(long ItemId, long WarehouseId, Func<ItemStock, GoodsMovement> Build);
}
