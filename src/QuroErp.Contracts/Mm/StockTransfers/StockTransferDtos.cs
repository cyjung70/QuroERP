namespace QuroErp.Contracts.Mm.StockTransfers;

/// <summary>Stock transfer API routes (MM-UC-018; the PDA move MM-UC-039 saves through the same POST).</summary>
public static class StockTransferRoutes
{
    /// <summary>
    /// GET ?from=&amp;to=&amp;transferNo=&amp;warehouseId=&amp;movType= : transfers, newest first, for any signed-in user.
    /// POST: a new transfer.
    /// </summary>
    public const string Base = "api/mm/stock-transfers";

    /// <summary>GET one transfer with its lines, PUT a change (updatedAt required), DELETE ?updatedAt=.</summary>
    public const string ById = Base + "/{id:long}";

    /// <summary>
    /// GET ?value=&amp;step=FROM|TO : reads a place scanned on the PDA move (MM-BR-922, MM-BR-923) for any signed-in user. FROM takes
    /// a Location or a Pallet label, TO a Location label.
    /// </summary>
    public const string ScanPlace = Base + "/scan-place";

    /// <summary>The most rows a list returns.</summary>
    public const int MaxListSize = 1000;
}

/// <summary>Transfer list conditions; every condition is optional. WarehouseId matches either side.</summary>
public sealed class StockTransferSearchQuery
{
    /// <summary>Transfer date from, inclusive.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Transfer date to, inclusive.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Start of the transfer number.</summary>
    public string? TransferNo { get; init; }

    public long? WarehouseId { get; init; }

    /// <summary>TR or LT; none lists both.</summary>
    public string? MovType { get; init; }
}

/// <summary>One transfer. MovType is TR (between warehouses) or LT (between Locations of one warehouse). Amt is the sum of its lines.</summary>
public sealed record StockTransferSummaryDto(
    long Id, string TransferNo, string MovType, DateOnly TransferDt, long FromWarehouseId, string FromWarehouseCode, long ToWarehouseId,
    string ToWarehouseCode, int LineCount, decimal Amt, string? Remarks, DateTimeOffset UpdatedAt);

/// <summary>
/// One line: qty of the item's Lot moves from the sending Location to the receiving one at Prc (the moving average cost at the
/// transfer) for Amt (MM-BR-706). PalletId names a Pallet moved whole (MM-BR-912). FromOnHandQty is the item's stock in the sending
/// warehouse now.
/// </summary>
public sealed record StockTransferLineDto(
    long Id, int Seq, long ItemId, string ItemCd, string? ItemName, string? Unit, decimal Qty, long LotId, string LotNo,
    long FromLocationId, string FromLocationCode, long ToLocationId, string ToLocationCode, long? PalletId, string? PalletNo, decimal Prc,
    decimal Amt, decimal FromOnHandQty);

/// <summary>A transfer with its lines in seq order.</summary>
public sealed record StockTransferDto(StockTransferSummaryDto Header, IReadOnlyList<StockTransferLineDto> Lines);

/// <summary>A value scanned on the PDA move and its step: FROM (where the stock comes from) or TO (where it goes).</summary>
public sealed class StockTransferScanQuery
{
    public string? Value { get; init; }

    public string? Step { get; init; }
}

/// <summary>
/// A scanned place: Kind LC (a Location) or PL (a Pallet, moved whole, MM-BR-912), with its warehouse and Location. Pallet is
/// filled for PL; its Qty is the qty the line must move.
/// </summary>
public sealed record StockTransferPlaceDto(
    string Kind, long WarehouseId, string WarehouseCode, long LocationId, string LocationCode, StockTransferPalletDto? Pallet);

/// <summary>The scanned Pallet: what it holds.</summary>
public sealed record StockTransferPalletDto(
    long Id, string PalletNo, long ItemId, string ItemCd, string? ItemName, long LotId, string LotNo, decimal Qty);

/// <summary>
/// A new transfer or a change. MovType null is LT when the transfer stays in the sending warehouse (no ToWarehouseId or the same
/// one) and TR when it goes to another (MM-BR-922). TransferDt null is today. ToWarehouseId null is the sending warehouse.
/// UpdatedAt is required on a change.
/// </summary>
public sealed record SaveStockTransferRequest(
    string? MovType, DateOnly? TransferDt, long? FromWarehouseId, long? ToWarehouseId, string? Remarks,
    IReadOnlyList<SaveStockTransferLineRequest>? Lines, DateTimeOffset? UpdatedAt = null);

/// <summary>
/// One line. LotId or LotNo names the Lot to move, neither is Lot '*' (MM-BR-363, MM-BR-357). FromLocationId and ToLocationId null
/// are Location '*' (MM-BR-713, MM-BR-715). A line with PalletId moves that Pallet whole: its qty must be the Pallet's, and the
/// Pallet's Location and Lot are used when the line names none (MM-BR-912).
/// </summary>
public sealed record SaveStockTransferLineRequest(
    long? ItemId, decimal? Qty, long? LotId = null, string? LotNo = null, long? FromLocationId = null, long? ToLocationId = null,
    long? PalletId = null);
