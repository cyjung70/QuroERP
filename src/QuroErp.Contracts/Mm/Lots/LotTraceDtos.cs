namespace QuroErp.Contracts.Mm.Lots;

/// <summary>Lot trace routes (MM-UC-040).</summary>
public static class LotTraceRoutes
{
    public const string Base = "api/mm/lot-trace";

    public const string Forward = "forward";

    public const string Backward = "backward";

    public const string ExportPdf = "export.pdf";

    public const string ExportXlsx = "export.xlsx";
}

/// <summary>Forward trace from one Lot of an item (MM-BR-366). Both are required; Lot '*' gives no rows.</summary>
public sealed class LotTraceForwardQuery
{
    public long? ItemId { get; init; }

    public string? LotNo { get; init; }
}

/// <summary>
/// Backward trace (MM-BR-367) from one of, in this order: a shipment or invoice number (all its lines), a shipment line, an
/// invoice line, or a customer's shipments and invoices dated From to To (both required with a customer).
/// </summary>
public sealed class LotTraceBackwardQuery
{
    public string? DocNo { get; init; }

    public long? ShipmentLineId { get; init; }

    public long? InvoiceLineId { get; init; }

    public long? CustomerId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

/// <summary>
/// A row of the trace tree. ParentKey is the Key of the row above it (null at the top), Level its depth from 0. DocType is LOT,
/// GR, SHIP, INV, ASM or MOVE; MovType the stock movement type of a movement row. Qty is the signed movement qty, or on an origin
/// row the received, assembled or linked qty. The screen, PDF and Excel show the same rows (MM-BR-369).
/// </summary>
public sealed record LotTraceRowDto(
    int Key, int? ParentKey, int Level, string DocType, string? DocNo, int? DocSeq, DateOnly? DocDt, string? MovType,
    string? WarehouseCode, string? PartyName, string ItemCd, string? ItemName, string LotNo, decimal? Qty);
