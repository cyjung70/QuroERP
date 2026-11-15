using System.Data;

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

/// <summary>Pallets (mm_pallet) and their history (mm_pallet_event, MM-BR-916). Callers pass their own transaction.</summary>
public interface IPalletRepository
{
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
