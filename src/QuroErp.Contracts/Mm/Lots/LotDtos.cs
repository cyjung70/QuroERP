namespace QuroErp.Contracts.Mm.Lots;

public static class LotRoutes
{
    public const string Base = "api/mm/lots";

    /// <summary>FIFO suggestion of the Locations and Lots of a decrease (MM-BR-716).</summary>
    public const string Suggestions = "api/mm/stock-suggestions";
}

/// <summary>Lot search of a stock document line: one item in one warehouse on one date (MM-BR-362). All are required.</summary>
public sealed class LotSearchQuery
{
    public long? ItemId { get; init; }

    public long? WarehouseId { get; init; }

    /// <summary>The document date: balances are as of the end of that day.</summary>
    public DateOnly? Date { get; init; }
}

/// <summary>
/// The balance of one (Location, Lot) above zero on the asked date, in FIFO order: Lot '*' first, then received date, Lot
/// number and Location code (MM-BR-362, MM-BR-716).
/// </summary>
public sealed record LotBalanceDto(long LotId, string LotNo, decimal OnHandQty, long LocationId = 0, string LocationCode = "*");

/// <summary>
/// FIFO suggestion for a line that decreases qty of an item in a warehouse (MM-BR-716). ItemId, WarehouseId and Qty are required;
/// Date is the document date and defaults to today.
/// </summary>
public sealed class StockSuggestionQuery
{
    public long? ItemId { get; init; }

    public long? WarehouseId { get; init; }

    /// <summary>The quantity to take, above zero.</summary>
    public decimal? Qty { get; init; }

    /// <summary>Balances are as of the end of this day.</summary>
    public DateOnly? Date { get; init; }
}

/// <summary>
/// One part of a suggested decrease: take Qty from this (Location, Lot). The parts add up to the asked quantity, or to less
/// when the warehouse does not have it (MM-BR-716).
/// </summary>
public sealed record StockSuggestionDto(long LocationId, string LocationCode, long LotId, string LotNo, decimal Qty);

/// <summary>
/// The FIFO suggestion of a decrease: the (Location, Lot) parts and ShortQty, the part of the asked quantity the warehouse
/// cannot give (0 when the lines cover it) (MM-BR-716).
/// </summary>
public sealed record StockSuggestionResultDto(IReadOnlyList<StockSuggestionDto> Lines, decimal ShortQty);
