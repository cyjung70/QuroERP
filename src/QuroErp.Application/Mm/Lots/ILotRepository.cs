using System.Data;
using QuroErp.Domain.Mm.Inventory;

namespace QuroErp.Application.Mm.Lots;

/// <summary>An mm_onhand key whose balance differs from the ledger sum of that key (MM-BR-605, MM-BR-354).</summary>
public sealed record OnHandMismatchRow(
    long ItemId, string ItemCd, long WarehouseId, string WarehouseCode, string LocationCode, string LotNo, decimal OnHandQty, decimal LedgerQty);

/// <summary>Lot search, stock suggestions and the stock key side of item lot and Location management (mm_lot, mm_onhand; MM lot.md).</summary>
public interface ILotRepository
{
    /// <summary>
    /// The (Location, Lot) balances of item + warehouse at the end of day asOf: the current balance minus later movements. Only
    /// balances above zero on Locations in use, with the Lot's received date (MM-BR-362, MM-BR-716). The caller orders them.
    /// withoutPallets leaves out what the key's ACTIVE Pallets hold: what a line that is not a Pallet line may take (MM-BR-916).
    /// </summary>
    Task<IReadOnlyList<StockBalance>> FindBalancesAsync(
        long itemId, long warehouseId, DateOnly asOf, bool withoutPallets, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>A balance of the item on a lot other than '*' is not 0 (MM-BR-351).</summary>
    Task<bool> HasLotStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>A balance of the item on a Location other than '*' is not 0 (MM-BR-718).</summary>
    Task<bool> HasLocationStockAsync(long itemId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// mm_onhand keys (item, warehouse, Location, Lot) whose balance differs from the ledger sum of the key, in item, warehouse,
    /// Location and lot code order. The stock reconciliation (MM-BR-605) lists these.
    /// </summary>
    Task<IReadOnlyList<OnHandMismatchRow>> FindOnHandMismatchesAsync(IDbTransaction transaction, CancellationToken cancellationToken);
}
