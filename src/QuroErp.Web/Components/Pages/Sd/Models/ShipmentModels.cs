using System.Globalization;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Sd.Models;

/// <summary>
/// Shipment header on the shipment screen (SD-UC-051). <see cref="ShipmentDto"/> is immutable, so the frame binds to this. The
/// customer is picked by name and the warehouse by code; the page keeps their ids.
/// </summary>
public sealed class ShipmentHeader
{
    public long Id { get; set; }
    public string? ShipNo { get; set; }
    public string? Status { get; set; }
    public string? BillingFlow { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateOnly? ShipDt { get; set; }
    public string? WarehouseCode { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string? ShipVia { get; set; }
    public string? ShipName { get; set; }
    public string? ShipAddr1 { get; set; }
    public string? ShipAddr2 { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipZipCode { get; set; }
    public string? ShipCountry { get; set; }
    public string? ShipPhone { get; set; }
    public string? Remarks { get; set; }
    public string? SalesInvoiceNo { get; set; }

    /// <summary>The list's calculated amount, shown only in the find dialog.</summary>
    public decimal ShipAmt { get; set; }

    /// <summary>A ship-then-bill line has been billed (SD-BR-168).</summary>
    public bool HasBilledLines { get; set; }

    /// <summary>The ship-and-bill invoice has a receipt or credit applied (SD-BR-104, SD-BR-167).</summary>
    public bool InvoiceHasPayments { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>A saved shipment keeps its customer and ship date (SD-BR-174).</summary>
    public bool IsSaved => Id != 0;

    public QuroAudit ToAudit() => new(CreatedBy, CreatedAt, UpdatedBy, UpdatedAt, null, null);

    /// <summary>A ship-and-bill shipment whose invoice was paid no longer changes; a bill-first shipment always can (SD-BR-177).</summary>
    public bool InvoiceLocksShipment => BillingFlow == ShipmentFlows.ShipAndBill && InvoiceHasPayments;

    /// <summary>A row of the find dialog: number, date, customer, status and amount.</summary>
    public static ShipmentHeader From(ShipmentSummaryDto shipment) => new()
    {
        Id = shipment.Id,
        ShipNo = shipment.ShipNo,
        Status = shipment.Status,
        BillingFlow = shipment.BillingFlow,
        CustomerId = shipment.CustomerId,
        CustomerName = shipment.CustomerName,
        ShipDt = shipment.ShipDt,
        CurrencyCode = shipment.CurrencyCode,
        SalesInvoiceNo = shipment.SalesInvoiceNo,
        ShipAmt = shipment.ShipAmt,
    };

    /// <param name="warehouseCodes">Warehouse codes by id.</param>
    public static ShipmentHeader From(ShipmentDto shipment, IReadOnlyDictionary<long, string> warehouseCodes) => new()
    {
        Id = shipment.Id,
        ShipNo = shipment.ShipNo,
        Status = shipment.Status,
        BillingFlow = shipment.BillingFlow,
        CustomerId = shipment.CustomerId,
        CustomerName = shipment.CustomerName,
        ShipDt = shipment.ShipDt,
        WarehouseCode = warehouseCodes.GetValueOrDefault(shipment.WarehouseId),
        CurrencyCode = shipment.CurrencyCode,
        ExchangeRate = shipment.ExchangeRate,
        ShipVia = shipment.ShipVia,
        ShipName = shipment.ShipName,
        ShipAddr1 = shipment.ShipAddr1,
        ShipAddr2 = shipment.ShipAddr2,
        ShipCity = shipment.ShipCity,
        ShipState = shipment.ShipState,
        ShipZipCode = shipment.ShipZipCode,
        ShipCountry = shipment.ShipCountry,
        ShipPhone = shipment.ShipPhone,
        Remarks = shipment.Remarks,
        SalesInvoiceNo = shipment.SalesInvoiceNo,
        HasBilledLines = shipment.Lines.Any(l => l.BilledQty != 0),
        InvoiceHasPayments = shipment.InvoiceHasPayments,
        UpdatedAt = shipment.UpdatedAt,
        CreatedAt = shipment.CreatedAt,
        CreatedBy = shipment.CreatedBy,
        UpdatedBy = shipment.UpdatedBy,
    };

    /// <summary>
    /// The save request with the kept lines in screen order (lines left out are deleted, SD-BR-171). The rate is left to the API:
    /// order and invoice lines bring theirs, direct lines take the latest rate of the currency (SD-BR-170, BC-BR-412).
    /// </summary>
    /// <param name="confirmCreditLimit">The answer to SD-W-309; null lets the API ask (SD-BR-309).</param>
    public SaveShipmentRequest ToRequest(bool isNew, long? warehouseId, IEnumerable<ShipmentLine> lines, bool? confirmCreditLimit) =>
        new(
            CustomerId ?? 0,
            ShipDt ?? DateOnly.FromDateTime(DateTime.Today),
            warehouseId,
            Clean(CurrencyCode),
            null,
            Clean(ShipVia),
            Clean(ShipName),
            Clean(ShipAddr1),
            Clean(ShipAddr2),
            Clean(ShipCity),
            Clean(ShipState),
            Clean(ShipZipCode),
            Clean(ShipCountry),
            Clean(ShipPhone),
            Clean(Remarks),
            [.. lines.Select(l => l.ToRequest())],
            isNew ? null : UpdatedAt,
            confirmCreditLimit);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A line of the shipment grid (sd.shipment.entry). Codes are shown; the ids are hidden columns. A line comes from an order line
/// (SD-BR-172), a bill-first invoice line (SD-BR-175) or is entered directly (SD-BR-173).
/// </summary>
public sealed class ShipmentLine
{
    public long Id { get; set; }
    public int? Seq { get; set; }
    public long? ItemId { get; set; }
    public string? ItemCd { get; set; }
    public string? ItemName { get; set; }
    public string? Unit { get; set; }
    public long? WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public long? LotId { get; set; }
    public string? LotNo { get; set; }
    public decimal? ShipQty { get; set; }
    public decimal? ShipPrc { get; set; }
    public decimal? DcRate { get; set; }
    public long? TaxCodeId { get; set; }
    public decimal? RemainingQty { get; set; }
    public decimal BilledQty { get; set; }
    public decimal? OnhandQty { get; set; }
    public long? SalesOrderLineId { get; set; }
    public string? SoNo { get; set; }
    public long? SalesInvoiceLineId { get; set; }
    public string? DnNo { get; set; }

    /// <summary>A saved line is not changed: it is deleted and added again (SD-BR-171).</summary>
    public bool IsSaved => Id != 0;

    /// <summary>
    /// A line that may be marked for delete: any line, except a billed line of a ship-then-bill shipment (SD-BR-168) and the lines of
    /// a ship-and-bill shipment whose invoice was paid (SD-BR-104, SD-BR-167). Bill-first shipments are free (SD-BR-177).
    /// </summary>
    public bool IsRemovable(ShipmentHeader? shipment) => shipment is null
        || (!shipment.InvoiceLocksShipment && (shipment.BillingFlow != ShipmentFlows.ShipThenBill || BilledQty == 0));

    /// <summary>
    /// The item and price of an imported line are the source line's (SD-BR-167, SD-BR-175), and a bill-first line stays in the
    /// consignment warehouse (SD-BR-176). A direct line has none of these locks (SD-BR-173).
    /// </summary>
    public bool IsCellLocked(string field)
    {
        var imported = SalesOrderLineId is not null || SalesInvoiceLineId is not null;
        return (imported && (Is(field, nameof(ItemCd)) || Is(field, nameof(ShipPrc))))
            || (SalesInvoiceLineId is not null && Is(field, nameof(WarehouseCode)));
    }

    public static ShipmentLine From(ShipmentLineDto line) => new()
    {
        Id = line.Id,
        Seq = line.Seq,
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        Unit = line.Unit,
        WarehouseId = line.WarehouseId,
        WarehouseCode = line.WarehouseCode,
        LotId = line.LotId,
        LotNo = line.LotNo,
        ShipQty = line.ShipQty,
        ShipPrc = line.ShipPrc,
        DcRate = line.DcRate,
        TaxCodeId = line.TaxCodeId,
        RemainingQty = line.RemainingQty,
        BilledQty = line.BilledQty,
        OnhandQty = line.OnhandQty,
        SalesOrderLineId = line.SalesOrderLineId,
        SoNo = line.SoNo,
        SalesInvoiceLineId = line.SalesInvoiceLineId,
        DnNo = line.DnNo,
    };

    /// <summary>An order line left to ship; the ship qty is what the import dialog says, by default the remaining qty (SD-BR-172).</summary>
    public static ShipmentLine From(OpenSalesOrderDto order, OpenSalesOrderLineDto line, decimal shipQty, long? warehouseId, string? warehouseCode) => new()
    {
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        Unit = line.Unit,
        WarehouseId = warehouseId,
        WarehouseCode = warehouseCode,
        ShipQty = shipQty,
        ShipPrc = line.SoPrc,
        DcRate = line.DcRate,
        TaxCodeId = line.TaxCodeId,
        RemainingQty = line.RemainingQty,
        SalesOrderLineId = line.SalesOrderLineId,
        SoNo = order.SoNo,
    };

    /// <summary>A bill-first invoice line left to ship, from the consignment warehouse (SD-BR-175, SD-BR-176).</summary>
    public static ShipmentLine From(UnshippedInvoiceLineDto line, decimal shipQty, long? consignmentId, string? consignmentCode) => new()
    {
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        Unit = line.Unit,
        WarehouseId = consignmentId,
        WarehouseCode = consignmentCode,
        ShipQty = shipQty,
        ShipPrc = line.Prc,
        DcRate = line.DcRate,
        TaxCodeId = line.TaxCodeId,
        RemainingQty = line.UnshippedQty,
        SalesInvoiceLineId = line.SalesInvoiceLineId,
        DnNo = line.DnNo,
    };

    /// <summary>
    /// Saved lines go back with their id and stored values; price and tax code are sent for direct lines only, the others take the
    /// source line's (SD-BR-173). A lot number typed or picked is sent by number.
    /// </summary>
    public SaveShipmentLineRequest ToRequest()
    {
        var direct = SalesOrderLineId is null && SalesInvoiceLineId is null;
        var lotNo = string.IsNullOrWhiteSpace(LotNo) ? null : LotNo.Trim().ToUpperInvariant();
        return new(
            Id == 0 ? null : Id, ItemId ?? 0, ItemName, WarehouseId, ShipQty ?? 0, direct || IsSaved ? ShipPrc : null,
            direct || IsSaved ? TaxCodeId : null, lotNo is null ? null : LotId, lotNo, SalesOrderLineId, SalesInvoiceLineId);
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>An order line in the order import dialog, with the order whose header the first import copies (SD-BR-170, SD-BR-172).</summary>
public sealed record ShipmentOrderLineChoice(OpenSalesOrderDto Order, OpenSalesOrderLineDto Line);

/// <summary>The billing flows of a shipment (SD-BR-174).</summary>
public static class ShipmentFlows
{
    public const string ShipAndBill = "SHIP_AND_BILL";
    public const string ShipThenBill = "SHIP_THEN_BILL";
    public const string BillThenShip = "BILL_THEN_SHIP";
}

/// <summary>Shipment statuses (SD-BR-166) and their chips. A void shipment does not change.</summary>
public static class ShipmentStatus
{
    public const string Open = "OP";
    public const string PartlyBilled = "PB";
    public const string Billed = "BL";
    public const string Void = "VO";

    public static IReadOnlyList<string> All { get; } = [Open, PartlyBilled, Billed, Void];

    public static ChipKind ChipOf(string? status) => status switch
    {
        PartlyBilled => ChipKind.Warn,
        Billed => ChipKind.Ok,
        Void => ChipKind.Danger,
        _ => ChipKind.Neutral,
    };

    /// <summary>A new shipment, or a stored one that is not void and whose ship-and-bill invoice has no payment (SD-BR-104, SD-BR-167).</summary>
    public static bool CanSave(ShipmentHeader shipment) => shipment.Status != Void && !shipment.InvoiceLocksShipment;

    /// <summary>
    /// A stored shipment that can still change (SD-BR-169) without billed lines of a ship-then-bill flow (SD-BR-168). A bill-first
    /// shipment voids whatever its invoice has received (SD-BR-177).
    /// </summary>
    public static bool CanVoid(ShipmentHeader shipment) =>
        shipment.IsSaved && CanSave(shipment) && !(shipment.BillingFlow == ShipmentFlows.ShipThenBill && shipment.HasBilledLines);
}

/// <summary>The number formats of the shipment line columns with the document decimals.</summary>
public static class ShipmentFormats
{
    public static IReadOnlyDictionary<string, string> Of(SalesOrderDecimals decimals) => new Dictionary<string, string>
    {
        ["shipQty"] = N(decimals.Qty),
        ["remainingQty"] = N(decimals.Qty),
        ["billedQty"] = N(decimals.Qty),
        ["onhandQty"] = N(decimals.Qty),
        ["shipPrc"] = N(decimals.Price),
    };

    private static string N(int decimals) => "N" + decimals.ToString(CultureInfo.InvariantCulture);
}
