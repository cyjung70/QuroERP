using System.Data;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Mm.Pallets;

/// <summary>A Pallet a goods receipt line makes (MM-BR-910). Its CREATE event takes the receipt date and number.</summary>
public sealed record NewPallet(
    string PalletNo, long ItemId, long LotId, long WarehouseId, long LocationId, decimal Qty, long GoodsReceiptLineId, DateOnly EventDt,
    string RefKey, int RefSeq);

/// <summary>
/// A Pallet of a receipt line. AsReceived: still as the receipt made it, ACTIVE with its first qty, warehouse and Location and no
/// event but CREATE (MM-BR-911).
/// </summary>
public sealed record ReceiptPalletRow(long Id, string PalletNo, long GoodsReceiptLineId, bool AsReceived);

/// <summary>What a Pallet label prints (MM-BR-918): the Pallet, its item, Lot, qty and the Lot's expiry.</summary>
public sealed record PalletLabelRow(long Id, string PalletNo, string ItemCd, string? ItemName, string LotNo, decimal Qty, DateOnly? ExpiryDt);

/// <summary>Search conditions, already trimmed and in upper case.</summary>
public sealed record PalletFilter(
    string? PalletNo, long? ItemId, string? LotNo, long? WarehouseId, long? LocationId, string? Status, int Limit);

/// <summary>A Pallet with the codes of its item, Lot, warehouse and Location, and the number of the Pallet it was split from.</summary>
public sealed record PalletRow(
    long Id, string PalletNo, long ItemId, string ItemCd, string? ItemName, long LotId, string LotNo, long WarehouseId, string WarehouseCode,
    long LocationId, string LocationCode, decimal Qty, string Status, string? ParentPalletNo, DateTimeOffset UpdatedAt)
{
    public PalletState ToState() => new(Id, PalletNo, ItemId, LotId, WarehouseId, LocationId, Qty, Status);
}

/// <summary>One history row, oldest first, with the number of the other Pallet of a split or merge.</summary>
public sealed record PalletEventRow(string EventType, DateOnly EventDt, decimal Qty, string? RelatedPalletNo, string? RefKey);

/// <summary>Pallets (mm_pallet) and their history (mm_pallet_event, MM-BR-916). Callers pass their own transaction.</summary>
public interface IPalletRepository
{
    /// <summary>Pallets that match every given condition, in number order, at most filter.Limit rows.</summary>
    Task<IReadOnlyList<PalletRow>> SearchAsync(PalletFilter filter, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The Pallet, or null.</summary>
    Task<PalletRow?> FindAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The Pallet with this exact number, or null.</summary>
    Task<PalletRow?> FindByNoAsync(string palletNo, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The history of the Pallet, oldest first.</summary>
    Task<IReadOnlyList<PalletEventRow>> FindEventsAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The Pallets that exist among the ids, locked until the transaction ends (in id order, so two callers cannot deadlock).</summary>
    Task<IReadOnlyList<PalletRow>> LockAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// MM-BR-914: a new ACTIVE Pallet with the number and qty on the source's item, Lot, warehouse and Location, whose parent is the
    /// source; the source keeps the rest. A SPLIT event on each side, minus on the source, each naming the other Pallet.
    /// </summary>
    /// <returns>The new Pallet's id.</returns>
    Task<long> SplitAsync(
        long sourceId, string palletNo, decimal qty, DateOnly eventDt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// MM-BR-915: each source goes to 0 and CLOSED and the target takes their qty. A MERGE event of minus the qty on each source and
    /// one of plus the qty on the target per source, each naming the other Pallet.
    /// </summary>
    Task MergeAsync(
        long targetId, IReadOnlyCollection<long> sourceIds, DateOnly eventDt, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>Inserts the Pallets and a CREATE event of each with its qty.</summary>
    Task InsertAsync(IReadOnlyList<NewPallet> pallets, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The Pallets of the given receipt lines, locked until the transaction ends, in number order.</summary>
    Task<IReadOnlyList<ReceiptPalletRow>> LockByReceiptLinesAsync(
        IReadOnlyCollection<long> goodsReceiptLineIds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Closes Pallets whose receipt line is deleted: qty 0, CLOSED, an ADJUST event of minus the qty, and no receipt line any more
    /// (MM-BR-911, MM-BR-916).
    /// </summary>
    Task CloseForDeletedLinesAsync(
        IReadOnlyCollection<long> palletIds, DateOnly eventDt, string refKey, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>The Pallet numbers of each line of the receipt, in number order.</summary>
    Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> FindNumbersByReceiptAsync(
        long goodsReceiptId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The label values of the receipt's ACTIVE Pallets, in line and number order.</summary>
    Task<IReadOnlyList<PalletLabelRow>> FindReceiptLabelsAsync(long goodsReceiptId, IDbTransaction transaction, CancellationToken cancellationToken);
}
