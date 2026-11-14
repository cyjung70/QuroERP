using System.Data;

namespace QuroErp.Application.Mm.Lots;

/// <summary>A lot balance of one item + warehouse as of a date.</summary>
public sealed record LotBalanceRow(long LotId, string LotNo, decimal OnHandQty);

/// <summary>An item + warehouse whose current stock differs from the sum of its lot balances (MM-BR-354).</summary>
public sealed record LotMismatchRow(long ItemId, string ItemCd, long WarehouseId, string WarehouseCode, decimal OnHandQty, decimal LotQty);

/// <summary>Lot search and the lot side of item lot management (mm_lot, lot rows of mm_onhand; MM lot.md).</summary>
public interface ILotRepository
{
    /// <summary>
    /// Lot balances of item + warehouse at the end of day asOf: the current balance minus later movements. Only balances above
    /// zero, in lot number order (MM-BR-362).
    /// </summary>
    Task<IReadOnlyList<LotBalanceRow>> FindBalancesAsync(
        long itemId, long warehouseId, DateOnly asOf, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>A warehouse of the item has stock below zero (MM-BR-351).</summary>
    Task<bool> HasNegativeOnHandAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>A lot balance of the item is not 0 (MM-BR-351).</summary>
    Task<bool> HasLotStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Turning lot management on: the current stock stays on Lot '*' (MM-BR-351), so there is nothing to move. The item row
    /// is locked by the caller, so no stock movement runs meanwhile.
    /// </summary>
    Task MigrateStockAsync(long itemId, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Lot-managed item + warehouse rows whose mm_onhand differs from the sum of their lot balances, in item and warehouse code
    /// order. The stock reconciliation (MM-BR-605) adds these to its differences (MM-BR-354).
    /// </summary>
    Task<IReadOnlyList<LotMismatchRow>> FindLotMismatchesAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}
