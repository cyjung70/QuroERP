namespace QuroErp.Contracts.Mm.Lots;

public static class LotRoutes
{
    public const string Base = "api/mm/lots";
}

/// <summary>Lot search of a stock document line: one item in one warehouse on one date (MM-BR-362). All are required.</summary>
public sealed class LotSearchQuery
{
    public long? ItemId { get; init; }

    public long? WarehouseId { get; init; }

    /// <summary>The document date: balances are as of the end of that day.</summary>
    public DateOnly? Date { get; init; }
}

/// <summary>A lot with a balance above zero on the asked date, in lot number order (MM-BR-362).</summary>
public sealed record LotBalanceDto(long LotId, string LotNo, decimal OnHandQty);
