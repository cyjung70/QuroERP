using System.Data;
using QuroErp.Application.Common;
using QuroErp.Application.Common.DocumentNumbers;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Mm.Lots;

namespace QuroErp.Application.Mm.Inventory;

/// <summary>
/// One stock change asked by a document line. Qty is signed (+ in, − out). Price null means the item's moving
/// average cost at that moment (issues); receipts pass their price. LotId or LotNo names the lot of a lot-managed item and is
/// ignored for other items: an increase with neither gets an automatic number, a decrease must name one (MM-BR-353, MM-BR-355).
/// A price difference (PV) has Qty 0 and the value change in Amount; a vendor return (VR) passes the original receipt price.
/// </summary>
public sealed record StockMovementRequest(
    string MovType, DateOnly DocumentDt, long ItemId, long WarehouseId, decimal Qty,
    decimal? Price = null, string? RefKey = null, int? RefSeq = null, long? LotId = null, string? LotNo = null,
    decimal? Amount = null);

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
    /// <summary>
    /// Posts original movements. Service items are skipped and set items are expanded into their components
    /// (MM-BR-007, MM-BR-008). A price difference (PV) changes only the item's stock value and moving average cost: no stock,
    /// lot or negative stock check, and a date in a closed month moves to the first open day (MM-BR-005, MM-BR-759). A vendor
    /// return (VR) is an issue at the price asked, checked like any issue (MM-BR-602, MM-BR-356, MM-BR-785).
    /// </summary>
    /// <exception cref="ArgumentException">A VR without a price, or a PV or VR of a set item (sets are not bought).</exception>
    /// <exception cref="QuroErp.Domain.Common.DomainException">
    /// MM-E-002 closed month, MM-E-606 wrong sign or zero qty, MM-E-001 not enough stock in DENY mode,
    /// MM-E-009 / MM-E-010 negative balance on a later date or at a month end (a set component error ends with the
    /// set item code), SD-E-509 a set with no components; for lot-managed items MM-E-352 a bad lot number, MM-E-355 a
    /// decrease without a lot, MM-E-356 a negative lot balance or a date before lot management started.
    /// </exception>
    /// <exception cref="ConcurrencyException">A set's components changed between reading and locking.</exception>
    public async Task<IReadOnlyList<GoodsMovement>> PostAsync(
        IReadOnlyList<StockMovementRequest> requests, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var closeYearMonth = await inventory.GetCloseYearMonthAsync(transaction, cancellationToken);
        requests = [.. requests.Select(r => MovementType.IsAmountOnly(r.MovType)
            ? r with { DocumentDt = InventoryClosing.OpenDateFor(closeYearMonth, r.DocumentDt) }
            : r)];
        foreach (var request in requests)
        {
            if (request.MovType == MovementType.VendorReturn && request.Price is null)
            {
                throw new ArgumentException("A vendor return posts at the original receipt price.", nameof(requests));
            }
            InventoryClosing.EnsureOpen(closeYearMonth, request.DocumentDt);
        }

        // inventory.md step 0: read the components first, then lock every item row in one pass in id order so two
        // documents cannot lock the same rows in a different order. Saving a set's components locks the set row too
        var requestIds = requests.Select(r => r.ItemId).Distinct().ToList();
        var read = await sets.GetComponentsAsync(requestIds, transaction, cancellationToken);
        var items = await LockItemsAsync(requestIds.Concat(read.Select(c => c.ComponentItemId)), transaction, cancellationToken);
        var setIds = requestIds.Where(id => items[id].IsSet).ToList();
        var components = setIds.Count == 0
            ? []
            : await sets.GetComponentsAsync(setIds, transaction, cancellationToken);
        if (!SameComponents(read.Where(c => setIds.Contains(c.SetItemId)), components))
        {
            throw new ConcurrencyException();
        }

        var lines = new List<Line>();
        foreach (var request in requests)
        {
            var item = items[request.ItemId];
            var amountOnly = MovementType.IsAmountOnly(request.MovType);
            if (!item.IsSet)
            {
                lines.Add(new Line(request.ItemId, request.WarehouseId, request.DocumentDt, "", request.Qty, request.LotId, request.LotNo, false, amountOnly,
                    stock => GoodsMovement.Create(
                        request.MovType, request.DocumentDt, request.ItemId, request.WarehouseId, request.Qty,
                        request.Price ?? stock.MovingAverageCost, request.RefKey, request.RefSeq, amount: request.Amount)));
                continue;
            }
            if (amountOnly || request.MovType == MovementType.VendorReturn)
            {
                throw new ArgumentException($"Set item {item.ItemCd} is not bought, so it has no {request.MovType} movement.", nameof(requests));
            }
            // Components move at their own moving average cost and remember the set (MM-BR-007)
            foreach (var (componentId, qty) in SetComponent.Expand(item.ItemCd, request.Qty, components.Where(c => c.SetItemId == item.ItemId)))
            {
                lines.Add(new Line(componentId, request.WarehouseId, request.DocumentDt, item.ItemCd, qty, null, null, false, false, stock => GoodsMovement.Create(
                    request.MovType, request.DocumentDt, componentId, request.WarehouseId, qty,
                    stock.MovingAverageCost, request.RefKey, request.RefSeq, item.ItemId)));
            }
        }

        var stockLines = lines.Where(l => items[l.ItemId].IsStockManaged).ToList();
        return await ApplyAsync(stockLines, items, closeYearMonth, user, transaction, cancellationToken);
    }

    /// <summary>
    /// Cancels movements: each gets a reversal dated reversalDt with the same type and price and the opposite sign
    /// (MM-BR-601). A reversal that decreases stock (e.g. a receipt reversal) is checked like an issue (MM-BR-602). A lot-managed
    /// item's reversal uses the original lot, or MIGRATED when the original is from before lot management (MM-BR-355).
    /// A price difference (PV) reversal negates the amount and, like the original, moves out of a closed month (MM-BR-759).
    /// </summary>
    /// <exception cref="QuroErp.Domain.Common.DomainException">
    /// MM-E-002 closed month, MM-E-606 already reversed or a reversal, MM-E-001 not enough stock in DENY mode,
    /// MM-E-009 / MM-E-010 negative balance on a later date or at a month end (a set component error ends with the
    /// set item code), MM-E-356 a negative lot balance.
    /// </exception>
    public async Task<IReadOnlyList<GoodsMovement>> ReverseAsync(
        IReadOnlyList<long> movementIds, DateOnly reversalDt, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var closeYearMonth = await inventory.GetCloseYearMonthAsync(transaction, cancellationToken);
        var amountOnlyDt = InventoryClosing.OpenDateFor(closeYearMonth, reversalDt);

        var saved = (await inventory.LockMovementsAsync(movementIds, transaction, cancellationToken)).ToDictionary(s => s.Movement.Id);
        var reversedNow = new HashSet<long>();
        var reversals = new List<GoodsMovement>();
        foreach (var id in movementIds)
        {
            var original = saved.GetValueOrDefault(id) ?? throw new InvalidOperationException($"Goods movement {id} does not exist.");
            var amountOnly = MovementType.IsAmountOnly(original.Movement.MovType);
            if (!amountOnly)
            {
                InventoryClosing.EnsureOpen(closeYearMonth, reversalDt);
            }
            // The same id twice in one call is a second reversal
            reversals.Add(original.Movement.Reverse(amountOnly ? amountOnlyDt : reversalDt, original.Reversed || !reversedNow.Add(id)));
        }

        // Set rows are locked with the components, in the same id order as posting, for the set code in errors
        var items = await LockItemsAsync(
            reversals.Select(r => r.ItemId).Concat(reversals.Where(r => r.SetItemId is not null).Select(r => r.SetItemId!.Value)),
            transaction, cancellationToken);
        var lines = reversals.Select(r => new Line(
            r.ItemId, r.WarehouseId, r.DocumentDt, r.SetItemId is { } setId ? items[setId].ItemCd : "", r.Qty, r.LotId, null, true,
            MovementType.IsAmountOnly(r.MovType), _ => r)).ToList();
        return await ApplyAsync(lines, items, closeYearMonth, user, transaction, cancellationToken);
    }

    // MM-BR-603: ① lock on-hand ② on-hand check ③ point-in-time and month-end check ④ update on-hand, item and ledger
    private async Task<IReadOnlyList<GoodsMovement>> ApplyAsync(
        IReadOnlyList<Line> lines, Dictionary<long, ItemStock> items, string? closeYearMonth, string user,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var mode = StockPolicy.ModeOrDefault((await settings.GetAsync(transaction, cancellationToken))?.NegativeStockMode);

        // Lock in a fixed order so two documents touching the same rows cannot deadlock
        var onHand = new Dictionary<(long ItemId, long WarehouseId), OnHandRow>();
        foreach (var key in lines.Select(l => (l.ItemId, l.WarehouseId)).Distinct().Order())
        {
            onHand[key] = await inventory.LockOnHandAsync(key.ItemId, key.WarehouseId, user, transaction, cancellationToken);
        }
        var qty = onHand.ToDictionary(e => e.Key, e => e.Value.OnHandQty);

        // Lots of lot-managed items, then their balances locked after the on-hand rows in the same fixed order (MM-BR-354)
        var lots = new List<LotRef?>(lines.Count);
        foreach (var line in lines)
        {
            // A price difference changes the item's value only, so it has no lot (MM-BR-360)
            lots.Add(items[line.ItemId].LotManaged && !line.AmountOnly
                ? await ResolveLotAsync(line, items[line.ItemId], user, transaction, cancellationToken)
                : null);
        }
        var lotQty = new Dictionary<(long ItemId, long WarehouseId, long LotId), decimal>();
        foreach (var key in lines.Zip(lots).Where(p => p.Second is not null).Select(p => (p.First.ItemId, p.First.WarehouseId, p.Second!.Id)).Distinct().Order())
        {
            lotQty[key] = await inventory.LockLotOnHandAsync(key.ItemId, key.WarehouseId, key.Id, user, transaction, cancellationToken);
        }

        var lastClosedMonthEnd = InventoryClosing.LastClosedMonthEnd(closeYearMonth);
        var documentNos = await numbers.NextAsync(
            DocumentNumberFormat.GoodsMovement, lines.Select(l => l.DocumentDt).ToList(), user, transaction, cancellationToken);
        var posted = new List<GoodsMovement>(lines.Count);
        foreach (var ((line, documentNo), lot) in lines.Zip(documentNos).Zip(lots))
        {
            var key = (line.ItemId, line.WarehouseId);
            var item = items[line.ItemId];
            var warehouseCode = onHand[key].WarehouseCode;
            var movement = line.Build(item);
            // A price difference does not change the quantity, so there is nothing to check (MM-BR-602)
            movement.NegativeStock = !line.AmountOnly
                && StockPolicy.CheckOnHand(mode, qty[key], movement.Qty, item.ItemCd, warehouseCode, line.SetItemCd);
            if (movement.Qty < 0)
            {
                // Earlier lines of this document are already in the ledger, so they count
                var (opening, balances) = await inventory.GetDailyBalancesAsync(
                    line.ItemId, line.WarehouseId, lastClosedMonthEnd, movement.DocumentDt, transaction, cancellationToken);
                movement.NegativeStock |= PointInTimeStock.Check(
                    mode, movement.DocumentDt, movement.Qty, opening, balances, item.ItemCd, warehouseCode, line.SetItemCd);
            }
            if (lot is not null)
            {
                // MM-BR-356: whatever the negative stock mode is
                var lotKey = (line.ItemId, line.WarehouseId, lot.Id);
                LotRules.EnsureManagedFrom(movement.DocumentDt, item.LotManagedFrom, item.ItemCd, warehouseCode, lot.LotNo, lotQty[lotKey]);
                if (movement.Qty < 0)
                {
                    var daily = await inventory.GetLotDailyQtyAsync(
                        line.ItemId, line.WarehouseId, lot.Id, movement.DocumentDt, transaction, cancellationToken);
                    LotRules.EnsureBalance(movement.DocumentDt, movement.Qty, lotQty[lotKey], daily, item.ItemCd, warehouseCode, lot.LotNo);
                }
                lotQty[lotKey] += movement.Qty;
                movement.LotId = lot.Id;
            }
            qty[key] += movement.Qty;
            item.Apply(movement.Qty, movement.Amount, movement.Prc, MovementType.RecalculatesCost(movement.MovType, movement.Qty));
            movement.DocumentNo = documentNo;
            await inventory.InsertMovementAsync(movement, user, transaction, cancellationToken);
            posted.Add(movement);
        }

        foreach (var ((itemId, warehouseId), onHandQty) in qty)
        {
            await inventory.UpdateOnHandAsync(itemId, warehouseId, onHandQty, user, transaction, cancellationToken);
        }
        foreach (var ((itemId, warehouseId, lotId), lotOnHandQty) in lotQty)
        {
            await inventory.UpdateLotOnHandAsync(itemId, warehouseId, lotId, lotOnHandQty, user, transaction, cancellationToken);
        }
        foreach (var itemId in lines.Select(l => l.ItemId).Distinct())
        {
            await inventory.UpdateItemStockAsync(items[itemId], user, transaction, cancellationToken);
        }
        return posted;
    }

    // MM-BR-353, MM-BR-355: the lot of one line of a lot-managed item. The item row is locked, so numbering cannot race
    private async Task<LotRef> ResolveLotAsync(Line line, ItemStock item, string user, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (line.Reversal)
        {
            // A movement from before lot management has no lot; its stock went to MIGRATED when it was turned on (MM-BR-351)
            return line.LotId is { } originalId
                ? await inventory.FindLotAsync(item.ItemId, originalId, null, transaction, cancellationToken)
                  ?? throw new InvalidOperationException($"Lot {originalId} of item {item.ItemId} does not exist.")
                : await inventory.EnsureLotAsync(item.ItemId, LotRules.Migrated, user, transaction, cancellationToken);
        }
        if (line.Qty < 0)
        {
            // A decrease takes one existing lot of the item, system lots included
            var number = line.LotNo?.Trim().ToUpperInvariant();
            return (line.LotId is null && string.IsNullOrEmpty(number)
                    ? null
                    : await inventory.FindLotAsync(item.ItemId, line.LotId, number, transaction, cancellationToken))
                ?? throw new DomainException(LotRules.RequiredCode, item.ItemCd);
        }
        if (line.LotId is { } lotId)
        {
            return await inventory.FindLotAsync(item.ItemId, lotId, null, transaction, cancellationToken)
                ?? throw new DomainException(LotRules.InvalidNumberCode, item.ItemCd, lotId);
        }
        var lotNo = LotRules.NormalizeNumber(item.ItemCd, line.LotNo)
            ?? LotRules.AutoNumber(line.DocumentDt, await inventory.GetLastAutoLotSeqAsync(
                item.ItemId, LotRules.AutoNumberPrefix(line.DocumentDt), transaction, cancellationToken));
        return await inventory.EnsureLotAsync(item.ItemId, lotNo, user, transaction, cancellationToken);
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

    private static bool SameComponents(IEnumerable<SetComponent> read, IEnumerable<SetComponent> locked) =>
        read.OrderBy(c => c.SetItemId).ThenBy(c => c.ComponentItemId)
            .SequenceEqual(locked.OrderBy(c => c.SetItemId).ThenBy(c => c.ComponentItemId));

    // Build runs at posting time so an issue takes the moving average cost after the lines before it.
    // SetItemCd is the set a component line came from, empty otherwise (last arg of stock errors). Qty is the signed change;
    // LotId and LotNo are what the caller asked for, or the original lot of a reversal. AmountOnly is a price difference (PV)
    private sealed record Line(
        long ItemId, long WarehouseId, DateOnly DocumentDt, string SetItemCd, decimal Qty, long? LotId, string? LotNo, bool Reversal,
        bool AmountOnly, Func<ItemStock, GoodsMovement> Build);
}
