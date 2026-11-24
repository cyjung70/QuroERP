using System.Data;
using QuroErp.Contracts.Mm.StockTransfers;

namespace QuroErp.Application.Mm.StockTransfers;

/// <summary>List conditions of mm_stock_transfer, already trimmed and in upper case, with at most Limit rows.</summary>
public sealed record StockTransferFilter(DateOnly? From, DateOnly? To, string? TransferNo, long? WarehouseId, string? MovType, int Limit);

/// <summary>One mm_stock_transfer row with its warehouse codes, line count and amount.</summary>
public sealed record StockTransferRow(
    long Id, string TransferNo, string MovType, DateOnly TransferDt, long FromWarehouseId, string FromWarehouseCode, long ToWarehouseId,
    string ToWarehouseCode, int LineCount, decimal Amt, string? Remarks, DateTimeOffset UpdatedAt)
{
    public StockTransferSummaryDto ToSummary() =>
        new(Id, TransferNo, MovType, TransferDt, FromWarehouseId, FromWarehouseCode, ToWarehouseId, ToWarehouseCode, LineCount, Amt, Remarks,
            UpdatedAt);
}

/// <summary>A transfer header to insert or to write over the stored one.</summary>
public sealed record StockTransferHeader(string MovType, DateOnly TransferDt, long FromWarehouseId, long ToWarehouseId, string? Remarks);

/// <summary>A new mm_stock_transfer_line row.</summary>
public sealed record NewStockTransferLine(
    int Seq, long ItemId, decimal Qty, long LotId, long FromLocationId, long ToLocationId, long? PalletId, decimal Prc, decimal Amt);

/// <summary>A warehouse a transfer can name: its code and whether it is in use.</summary>
public sealed record TransferWarehouse(long Id, string WarehouseCode, bool InUse);

/// <summary>A Location found by its label (MM-BR-918): its warehouse, and whether both are in use.</summary>
public sealed record ScannedLocation(
    long WarehouseId, string WarehouseCode, bool WarehouseInUse, long LocationId, string LocationCode, bool LocationInUse);

/// <summary>A Pallet found by its number, with where it is and its status.</summary>
public sealed record ScannedPallet(
    long Id, string PalletNo, long ItemId, string ItemCd, string? ItemName, long LotId, string LotNo, decimal Qty, string Status,
    long WarehouseId, string WarehouseCode, bool WarehouseInUse, long LocationId, string LocationCode, bool LocationInUse);

/// <summary>mm_stock_transfer and mm_stock_transfer_line, and the Pallet moves of their lines (MM-BR-705~708, MM-BR-912).</summary>
public interface IStockTransferRepository
{
    Task<IReadOnlyList<StockTransferRow>> SearchAsync(StockTransferFilter filter, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The row, locked when forUpdate so two changes of one transfer wait for each other.</summary>
    Task<StockTransferRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The lines in seq order, each with the item's stock in the sending warehouse now.</summary>
    Task<IReadOnlyList<StockTransferLineDto>> FindLinesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransferWarehouse>> FindWarehousesAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(string transferNo, StockTransferHeader header, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Writes the header over the stored one and deletes its lines; false when updatedAt is no longer the stored one.</summary>
    Task<bool> UpdateAsync(
        long id, StockTransferHeader header, DateTimeOffset updatedAt, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertLinesAsync(long id, IReadOnlyList<NewStockTransferLine> lines, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the transfer and its lines; false when updatedAt is no longer the stored one.</summary>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The ids of the original movements of the transfer number that are not reversed yet, in id order.</summary>
    Task<IReadOnlyList<long>> FindOpenMovementIdsAsync(string transferNo, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The Location with this warehouse code and Location code, as a Location label carries them.</summary>
    Task<ScannedLocation?> FindScannedLocationAsync(
        string warehouseCode, string locationCode, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<ScannedPallet?> FindScannedPalletAsync(string palletNo, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>MM-BR-912: puts the Pallet at the warehouse and Location and adds its MOVE event.</summary>
    Task MovePalletAsync(
        long palletId, long warehouseId, long locationId, DateOnly eventDt, string refKey, int refSeq, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);
}
