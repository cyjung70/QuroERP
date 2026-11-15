namespace QuroErp.Contracts.Mm.Labels;

/// <summary>Barcode label print API routes (MM-UC-037).</summary>
public static class LabelPrintRoutes
{
    /// <summary>POST a print: ZPL text as JSON or a PDF. Needs Full Access on the screen.</summary>
    public const string Base = "api/mm/label-prints";

    /// <summary>GET ?labelType=&amp;goodsReceiptId=&amp;itemId=&amp;warehouseId= : what can be printed, for any signed-in user.</summary>
    public const string Targets = Base + "/targets";

    /// <summary>GET ?from=&amp;to=&amp;labelType= : the print history, newest first, for any signed-in user.</summary>
    public const string History = Base + "/history";

    /// <summary>The most rows a target or history list returns.</summary>
    public const int MaxListSize = 1000;
}

/// <summary>Conditions of the print targets. LabelType is required; the others narrow the list where the type has them.</summary>
public sealed class LabelTargetQuery
{
    /// <summary>PALLET, ITEM, LOT or LOCATION.</summary>
    public string? LabelType { get; init; }

    /// <summary>Pallets made by the receipt, the items on its lines, the Lots it received.</summary>
    public long? GoodsReceiptId { get; init; }

    /// <summary>Pallets and Lots of the item, or the item itself.</summary>
    public long? ItemId { get; init; }

    /// <summary>Pallets in the warehouse, or the warehouse's Locations (required for LOCATION).</summary>
    public long? WarehouseId { get; init; }
}

/// <summary>
/// One thing that can be printed. Code is the Pallet number, item code, Lot number or Location code; Name is the item or Location
/// name. The other values are those the label prints, where the type has them.
/// </summary>
public sealed record LabelTargetDto(
    long Id, string LabelType, string Code, string? Name, string? ItemCd, string? LotNo, decimal? Qty, DateOnly? ExpiryDt,
    string? WarehouseCode, string? LocationCode);

/// <summary>A target to print and how many copies of it; a Pallet copy is its label and its DataMatrix label (MM-BR-918).</summary>
public sealed record LabelPrintTarget(long Id, int Copies);

/// <summary>
/// A print of one label type. OutputKind (ZPL or PDF) overrides the format's, e.g. PDF when the printer cannot be reached;
/// null uses the format's. PrinterName is kept in the history.
/// </summary>
public sealed record LabelPrintRequest(string? LabelType, IReadOnlyList<LabelPrintTarget>? Targets, string? OutputKind, string? PrinterName);

/// <summary>A ZPL print: the text the client sends to the Zebra printer.</summary>
public sealed record LabelPrintZplDto(string OutputKind, string Zpl);

/// <summary>Conditions of the print history: printed on these company dates, of this label type.</summary>
public sealed class LabelPrintHistoryQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public string? LabelType { get; init; }
}

/// <summary>One printed target (mm_label_print, MM-BR-919). RefText is its Pallet number, item code, Lot number or Location code.</summary>
public sealed record LabelPrintHistoryDto(
    long Id, string LabelType, long RefId, string? RefText, int Copies, string OutputKind, string? PrinterName, DateTimeOffset PrintedAt,
    string PrintedBy);
