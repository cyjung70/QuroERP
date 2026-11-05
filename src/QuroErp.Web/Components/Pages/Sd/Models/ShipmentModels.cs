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

    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>A saved shipment keeps its customer and ship date (SD-BR-174).</summary>
    public bool IsSaved => Id != 0;

    public QuroAudit ToAudit() => new(CreatedBy, CreatedAt, UpdatedBy, UpdatedAt, null, null);

    /// <summary>A row of the find dialog: number, date, customer, status and invoice.</summary>
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
    public string? TaxCode { get; set; }
    public decimal? ShipAmt { get; set; }
    public decimal BilledQty { get; set; }
    public decimal? OnhandQty { get; set; }
    public long? SalesOrderLineId { get; set; }
    public long? SalesInvoiceLineId { get; set; }

    /// <summary>The source line: order number-line or invoice number-line; empty for a direct line.</summary>
    public string? Source { get; set; }

    /// <summary>A saved line is not changed: it is deleted and added again (SD-BR-171).</summary>
    public bool IsSaved => Id != 0;

    /// <summary>
    /// A line that may be marked for delete: any line, except a billed line of a ship-then-bill shipment (SD-BR-168). The other
    /// flows check their invoice on save (SD-BR-104).
    /// </summary>
    public bool IsRemovable(string? billingFlow) => billingFlow != ShipmentFlows.ShipThenBill || BilledQty == 0;

    public static ShipmentLine From(ShipmentLineDto line, IReadOnlyDictionary<long, string> taxCodes) => new()
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
        TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
        BilledQty = line.BilledQty,
        OnhandQty = line.OnhandQty,
        SalesOrderLineId = line.SalesOrderLineId,
        SalesInvoiceLineId = line.SalesInvoiceLineId,
        Source = SourceOf(line.SoNo, line.SoSeq) ?? SourceOf(line.DnNo, line.DnSeq),
    };

    /// <summary>An order line left to ship, with its remaining qty as the ship qty (SD-BR-172).</summary>
    public static ShipmentLine From(
        OpenSalesOrderDto order, OpenSalesOrderLineDto line, long? warehouseId, string? warehouseCode, IReadOnlyDictionary<long, string> taxCodes) => new()
        {
            ItemId = line.ItemId,
            ItemCd = line.ItemCd,
            ItemName = line.ItemName,
            Unit = line.Unit,
            WarehouseId = warehouseId,
            WarehouseCode = warehouseCode,
            ShipQty = line.RemainingQty,
            ShipPrc = line.SoPrc,
            DcRate = line.DcRate,
            TaxCodeId = line.TaxCodeId,
            TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
            SalesOrderLineId = line.SalesOrderLineId,
            Source = SourceOf(order.SoNo, line.Seq),
        };

    /// <summary>A bill-first invoice line left to ship, from the consignment warehouse (SD-BR-175, SD-BR-176).</summary>
    public static ShipmentLine From(
        UnshippedInvoiceLineDto line, long? consignmentId, string? consignmentCode, IReadOnlyDictionary<long, string> taxCodes) => new()
        {
            ItemId = line.ItemId,
            ItemCd = line.ItemCd,
            ItemName = line.ItemName,
            WarehouseId = consignmentId,
            WarehouseCode = consignmentCode,
            ShipQty = line.UnshippedQty,
            ShipPrc = line.Prc,
            DcRate = line.DcRate,
            TaxCodeId = line.TaxCodeId,
            TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
            SalesInvoiceLineId = line.SalesInvoiceLineId,
            Source = SourceOf(line.DnNo, line.Seq),
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

    private static string? SourceOf(string? number, int? seq) =>
        number is null ? null : seq is { } s ? string.Create(CultureInfo.InvariantCulture, $"{number}-{s}") : number;
}

/// <summary>A bill-first invoice with lines left to ship, a row of the invoice import dialog (SD-BR-175).</summary>
public sealed record ShipmentInvoiceChoice(string DnNo, string CurrencyCode, decimal ExchangeRate, IReadOnlyList<UnshippedInvoiceLineDto> Lines)
{
    /// <summary>The lines grouped by invoice, in the order the API returned them.</summary>
    public static IReadOnlyList<ShipmentInvoiceChoice> Of(IEnumerable<UnshippedInvoiceLineDto> lines) =>
        [.. lines.GroupBy(l => l.SalesInvoiceId)
            .Select(g => new ShipmentInvoiceChoice(g.First().DnNo, g.First().CurrencyCode, g.First().ExchangeRate, [.. g.OrderBy(l => l.Seq)]))];
}

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
        Open => ChipKind.Brand,
        PartlyBilled => ChipKind.Warn,
        Billed => ChipKind.Ok,
        _ => ChipKind.Neutral,
    };

    /// <summary>A new shipment, or a stored one that is not void.</summary>
    public static bool CanSave(ShipmentHeader shipment) => shipment.Status != Void;

    /// <summary>A stored shipment that is not void (SD-BR-169); the API checks billing and payments.</summary>
    public static bool CanVoid(ShipmentHeader shipment) => shipment.IsSaved && shipment.Status != Void;
}

/// <summary>The number formats of the shipment line columns with the document decimals.</summary>
public static class ShipmentFormats
{
    public static IReadOnlyDictionary<string, string> Of(SalesOrderDecimals decimals) => new Dictionary<string, string>
    {
        ["shipQty"] = N(decimals.Qty),
        ["billedQty"] = N(decimals.Qty),
        ["onhandQty"] = N(decimals.Qty),
        ["shipPrc"] = N(decimals.Price),
        ["shipAmt"] = N(decimals.Amount),
    };

    private static string N(int decimals) => "N" + decimals.ToString(CultureInfo.InvariantCulture);
}
