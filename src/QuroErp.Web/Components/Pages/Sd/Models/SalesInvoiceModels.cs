using System.Globalization;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Web.Components.Screens;

namespace QuroErp.Web.Components.Pages.Sd.Models;

/// <summary>
/// Sales invoice header on the sales invoice screen (SD-UC-003). <see cref="SalesInvoiceDto"/> is immutable, so the frame binds to
/// this. The customer is picked by name and the warehouse by code; the page keeps their ids. The bill-to address and discount
/// status are the customer's, shown only.
/// </summary>
public sealed class SalesInvoiceHeader
{
    public long Id { get; set; }
    public string? DnNo { get; set; }
    public string? Status { get; set; }
    public string? BillingFlow { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateOnly? DnDt { get; set; }
    public DateOnly? DueDt { get; set; }
    public string? WarehouseCode { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string? Rep { get; set; }
    public string? Terms { get; set; }
    public string? ShipVia { get; set; }
    public string? FreightCharge { get; set; }
    public string? ShipName { get; set; }
    public string? ShipAddr1 { get; set; }
    public string? ShipAddr2 { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipZipCode { get; set; }
    public string? ShipCountry { get; set; }
    public string? ShipPhone { get; set; }
    public string? ShipFax { get; set; }
    public string? Remarks { get; set; }

    /// <summary>The US exemption certificate the invoice prints (SD-BR-133); empty lets the API take the customer's.</summary>
    public string? TaxExemptionCertNo { get; set; }

    /// <summary>The source documents: the order of bill-first lines, the shipment that made a ship-and-bill invoice.</summary>
    public string? SoNo { get; set; }
    public string? ShipNo { get; set; }

    public decimal TotDnAmt { get; set; }
    public decimal PaymentApplied { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal BalAmt { get; set; }
    public decimal AvailableCredit { get; set; }

    /// <summary>Unshipped cancellations of a bill-first invoice and their Credit Memos (SD-BR-145, SD-BR-146).</summary>
    public IReadOnlyList<SalesInvoiceCancellationDto> Cancellations { get; set; } = [];

    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>A saved invoice keeps its customer and date (SD-BR-119).</summary>
    public bool IsSaved => Id != 0;

    /// <summary>The invoice a ship-and-bill shipment made: its shipment lines change on the shipment (SD-BR-136).</summary>
    public bool MadeByShipment => BillingFlow == SalesInvoiceFlows.ShipAndBill;

    public QuroAudit ToAudit() => new(CreatedBy, CreatedAt, UpdatedBy, UpdatedAt, null, null);

    /// <summary>A row of the find dialog: number, date, customer, status and total.</summary>
    public static SalesInvoiceHeader From(SalesInvoiceSummaryDto invoice) => new()
    {
        Id = invoice.Id,
        DnNo = invoice.DnNo,
        Status = invoice.Status,
        BillingFlow = invoice.BillingFlow,
        CustomerId = invoice.CustomerId,
        CustomerName = invoice.CustomerName,
        DnDt = invoice.DnDt,
        CurrencyCode = invoice.CurrencyCode,
        TotDnAmt = invoice.TotDnAmt,
        BalAmt = invoice.BalAmt,
    };

    /// <param name="warehouseCodes">Warehouse codes by id.</param>
    public static SalesInvoiceHeader From(SalesInvoiceDto invoice, IReadOnlyDictionary<long, string> warehouseCodes) => new()
    {
        Id = invoice.Id,
        DnNo = invoice.DnNo,
        Status = invoice.Status,
        BillingFlow = invoice.BillingFlow,
        CustomerId = invoice.CustomerId,
        CustomerName = invoice.CustomerName,
        DnDt = invoice.DnDt,
        DueDt = invoice.DueDt,
        WarehouseCode = warehouseCodes.GetValueOrDefault(invoice.WarehouseId),
        CurrencyCode = invoice.CurrencyCode,
        ExchangeRate = invoice.ExchangeRate,
        Rep = invoice.Rep,
        Terms = invoice.Terms,
        ShipVia = invoice.ShipVia,
        FreightCharge = invoice.FreightCharge,
        ShipName = invoice.ShipName,
        ShipAddr1 = invoice.ShipAddr1,
        ShipAddr2 = invoice.ShipAddr2,
        ShipCity = invoice.ShipCity,
        ShipState = invoice.ShipState,
        ShipZipCode = invoice.ShipZipCode,
        ShipCountry = invoice.ShipCountry,
        ShipPhone = invoice.ShipPhone,
        ShipFax = invoice.ShipFax,
        Remarks = invoice.Remarks,
        TaxExemptionCertNo = invoice.TaxExemptionCertNo,
        SoNo = invoice.SoNo,
        ShipNo = invoice.ShipNo,
        TotDnAmt = invoice.TotDnAmt,
        PaymentApplied = invoice.PaymentApplied,
        CreditApplied = invoice.CreditApplied,
        BalAmt = invoice.BalAmt,
        AvailableCredit = invoice.AvailableCredit,
        Cancellations = invoice.Cancellations,
        UpdatedAt = invoice.UpdatedAt,
        CreatedAt = invoice.CreatedAt,
        CreatedBy = invoice.CreatedBy,
        UpdatedBy = invoice.UpdatedBy,
    };

    /// <summary>
    /// The save request with the kept lines in screen order (lines left out are deleted, SD-BR-107). The rate is the screen's for
    /// a new invoice of direct lines; shipments and orders bring theirs, and a saved invoice keeps its own (SD-BR-131).
    /// </summary>
    public SaveSalesInvoiceRequest ToRequest(bool isNew, long? warehouseId, IEnumerable<SalesInvoiceLine> lines, SalesOrderSaveAnswers answers) =>
        new(
            CustomerId ?? 0,
            DnDt ?? DateOnly.FromDateTime(DateTime.Today),
            DueDt,
            Clean(CurrencyCode),
            isNew ? ExchangeRate : null,
            warehouseId,
            Clean(Rep),
            Clean(Terms),
            Clean(ShipVia),
            Clean(FreightCharge),
            Clean(ShipName),
            Clean(ShipAddr1),
            Clean(ShipAddr2),
            Clean(ShipCity),
            Clean(ShipState),
            Clean(ShipZipCode),
            Clean(ShipCountry),
            Clean(ShipPhone),
            Clean(ShipFax),
            Clean(Remarks),
            [.. lines.Select(l => l.ToRequest())],
            isNew ? null : UpdatedAt,
            answers.UpdateCustomerShipTo,
            answers.ConfirmCreditLimit,
            Clean(TaxExemptionCertNo));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A line of the sales invoice grid (sd.sales-invoice.entry). Codes are shown; the ids are hidden columns. A line bills a
/// shipment line (SD-BR-115), a bill-first order line (SD-BR-141) or is entered directly for an item without stock (SD-BR-116).
/// </summary>
public sealed class SalesInvoiceLine
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
    public decimal? DnQty { get; set; }
    public decimal? DnPrc { get; set; }
    public string? DcCd { get; set; }
    public decimal? DcRate { get; set; }
    public decimal? StdPrc { get; set; }
    public long? TaxCodeId { get; set; }
    public string? TaxCode { get; set; }
    public decimal? DnAmt { get; set; }
    public long? ShipmentLineId { get; set; }
    public long? SalesOrderLineId { get; set; }

    /// <summary>Shipped, cancelled and unshipped qty of a bill-first line (SD-BR-143); empty on other lines.</summary>
    public decimal? ShippedQty { get; set; }
    public decimal? CancelledQty { get; set; }
    public decimal? UnshippedQty { get; set; }

    /// <summary>The source line: shipment number-line or order number-line; empty for a direct line.</summary>
    public string? Source { get; set; }

    public bool IsSaved => Id != 0;

    /// <summary>A bill-first line bills an order line without a shipment line (SD-BR-141).</summary>
    public bool IsBillFirst => ShipmentLineId is null && SalesOrderLineId is not null;

    public bool IsDirect => ShipmentLineId is null && SalesOrderLineId is null;

    /// <summary>
    /// A line that may be marked for delete: not a shipment line of a ship-and-bill invoice (SD-BR-136), not a bill-first line
    /// with shipped or cancelled qty (SD-BR-144).
    /// </summary>
    public bool IsRemovable(string? billingFlow) =>
        !(billingFlow == SalesInvoiceFlows.ShipAndBill && ShipmentLineId is not null)
        && !(IsBillFirst && ((ShippedQty ?? 0) != 0 || (CancelledQty ?? 0) != 0));

    public static SalesInvoiceLine From(SalesInvoiceLineDto line, IReadOnlyDictionary<long, string> taxCodes)
    {
        var billFirst = line.ShipmentLineId is null && line.SalesOrderLineId is not null;
        return new()
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
            DnQty = line.DnQty,
            DnPrc = line.DnPrc,
            DcCd = line.DcCd,
            DcRate = line.DcRate,
            StdPrc = line.StdPrc,
            TaxCodeId = line.TaxCodeId,
            TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
            DnAmt = line.DnAmt,
            ShipmentLineId = line.ShipmentLineId,
            SalesOrderLineId = line.SalesOrderLineId,
            ShippedQty = billFirst ? line.ShippedQty : null,
            CancelledQty = billFirst ? line.CancelledQty : null,
            UnshippedQty = billFirst ? line.UnshippedQty : null,
            Source = SourceOf(line.ShipNo, line.ShipSeq) ?? SourceOf(line.SoNo, line.SoSeq),
        };
    }

    /// <summary>A shipment line left to bill, with its unbilled qty (SD-BR-115, SD-BR-406).</summary>
    public static SalesInvoiceLine From(UnbilledShipmentLineDto line, string? warehouseCode, IReadOnlyDictionary<long, string> taxCodes) => new()
    {
        ItemId = line.ItemId,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        WarehouseId = line.WarehouseId,
        WarehouseCode = warehouseCode,
        DnQty = line.UnbilledQty,
        DnPrc = line.ShipPrc,
        DcCd = line.DcCd,
        DcRate = line.DcRate,
        TaxCodeId = line.TaxCodeId,
        TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
        ShipmentLineId = line.ShipmentLineId,
        Source = SourceOf(line.ShipNo, line.Seq),
    };

    /// <summary>A bill-first order line left to bill, with its remaining qty, from the header warehouse (SD-BR-141).</summary>
    public static SalesInvoiceLine From(
        OpenBillFirstOrderLineDto line, long? warehouseId, string? warehouseCode, IReadOnlyDictionary<long, string> taxCodes) => new()
        {
            ItemId = line.ItemId,
            ItemCd = line.ItemCd,
            ItemName = line.ItemName,
            Unit = line.Unit,
            WarehouseId = warehouseId,
            WarehouseCode = warehouseCode,
            DnQty = line.RemainingQty,
            DnPrc = line.SoPrc,
            DcCd = line.DcCd,
            DcRate = line.DcRate,
            TaxCodeId = line.TaxCodeId,
            TaxCode = taxCodes.GetValueOrDefault(line.TaxCodeId),
            SalesOrderLineId = line.SalesOrderLineId,
            Source = SourceOf(line.SoNo, line.Seq),
        };

    /// <summary>
    /// Saved lines go back with their id and stored values. Price and discount are sent for direct lines only, the others take the
    /// source line's (SD-BR-406); the tax code can be changed on any new line (SD-BR-132). The warehouse and lot are sent for
    /// bill-first and direct lines; a shipment line keeps the shipment's.
    /// </summary>
    public SaveSalesInvoiceLineRequest ToRequest()
    {
        var own = IsDirect || IsSaved;
        var lotNo = string.IsNullOrWhiteSpace(LotNo) ? null : LotNo.Trim().ToUpperInvariant();
        return new(
            Id == 0 ? null : Id, ItemId ?? 0, ItemName, DnQty ?? 0, own ? DnPrc : null, own ? DcCd : null, TaxCodeId,
            ShipmentLineId is null || IsSaved ? WarehouseId : null, lotNo is null ? null : LotId, lotNo, ShipmentLineId, SalesOrderLineId);
    }

    private static string? SourceOf(string? number, int? seq) =>
        number is null ? null : seq is { } s ? string.Create(CultureInfo.InvariantCulture, $"{number}-{s}") : number;
}

/// <summary>A shipment with lines left to bill, a row of the shipment import dialog (SD-BR-115).</summary>
public sealed record InvoiceShipmentChoice(string ShipNo, DateOnly ShipDt, IReadOnlyList<UnbilledShipmentLineDto> Lines)
{
    /// <summary>The lines grouped by shipment, in the order the API returned them.</summary>
    public static IReadOnlyList<InvoiceShipmentChoice> Of(IEnumerable<UnbilledShipmentLineDto> lines) =>
        [.. lines.GroupBy(l => l.ShipmentId).Select(g => new InvoiceShipmentChoice(g.First().ShipNo, g.First().ShipDt, [.. g.OrderBy(l => l.Seq)]))];
}

/// <summary>A bill-first order with lines left to bill, a row of the order import dialog (SD-BR-141).</summary>
public sealed record InvoiceOrderChoice(string SoNo, DateOnly SoDt, string CurrencyCode, decimal ExchangeRate, IReadOnlyList<OpenBillFirstOrderLineDto> Lines)
{
    public OpenBillFirstOrderLineDto First => Lines[0];

    /// <summary>The lines grouped by order, in the order the API returned them.</summary>
    public static IReadOnlyList<InvoiceOrderChoice> Of(IEnumerable<OpenBillFirstOrderLineDto> lines) =>
        [.. lines.GroupBy(l => l.SalesOrderId)
            .Select(g => new InvoiceOrderChoice(g.First().SoNo, g.First().SoDt, g.First().CurrencyCode, g.First().ExchangeRate, [.. g.OrderBy(l => l.Seq)]))];
}

/// <summary>The billing flows of an invoice (SD-BR-115, SD-BR-136, SD-BR-141).</summary>
public static class SalesInvoiceFlows
{
    public const string ShipAndBill = "SHIP_AND_BILL";
    public const string ShipThenBill = "SHIP_THEN_BILL";
    public const string BillThenShip = "BILL_THEN_SHIP";
}

/// <summary>Sales invoice statuses (SD-BR-105), their chips and the commands each allows.</summary>
public static class SalesInvoiceStatus
{
    public const string Open = "OP";
    public const string PartlyPaid = "PA";
    public const string Closed = "CL";
    public const string Void = "VO";

    public static IReadOnlyList<string> All { get; } = [Open, PartlyPaid, Closed, Void];

    public static ChipKind ChipOf(string? status) => status switch
    {
        Open => ChipKind.Brand,
        PartlyPaid => ChipKind.Warn,
        Closed => ChipKind.Ok,
        _ => ChipKind.Neutral,
    };

    /// <summary>A new invoice, or a stored one without receipts or credit applied (SD-BR-104, SD-BR-261).</summary>
    public static bool CanSave(SalesInvoiceHeader invoice) => invoice.Status is null or Open;

    /// <summary>
    /// Delete and void: a stored open invoice not made by a shipment (SD-BR-113) whose bill-first lines have nothing shipped or
    /// cancelled (SD-BR-144).
    /// </summary>
    public static bool CanVoid(SalesInvoiceHeader invoice, IEnumerable<SalesInvoiceLine> lines) =>
        invoice.IsSaved && invoice.Status == Open && !invoice.MadeByShipment
        && !lines.Any(l => l.IsSaved && l.IsBillFirst && ((l.ShippedQty ?? 0) != 0 || (l.CancelledQty ?? 0) != 0));

    /// <summary>SD-BR-145: a stored bill-first invoice in use with a saved line that has unshipped qty.</summary>
    public static bool CanCancelUnshipped(SalesInvoiceHeader invoice, IEnumerable<SalesInvoiceLine> lines) =>
        invoice.IsSaved && invoice.Status != Void && invoice.BillingFlow == SalesInvoiceFlows.BillThenShip
        && lines.Any(l => l.IsSaved && (l.UnshippedQty ?? 0) > 0);
}

/// <summary>One line of the unshipped cancellation dialog: the line, its unshipped qty and the qty to cancel (SD-BR-145).</summary>
public sealed class UnshippedCancelRow
{
    public long SalesInvoiceLineId { get; init; }
    public int Seq { get; init; }
    public string? ItemCd { get; init; }
    public string? ItemName { get; init; }
    public decimal UnshippedQty { get; init; }
    public decimal? Qty { get; set; }

    public static UnshippedCancelRow From(SalesInvoiceLine line) => new()
    {
        SalesInvoiceLineId = line.Id,
        Seq = line.Seq ?? 0,
        ItemCd = line.ItemCd,
        ItemName = line.ItemName,
        UnshippedQty = line.UnshippedQty ?? 0,
    };

    /// <summary>
    /// The cancellation request: the rows with a qty, and the eight reasons (SD-BR-208). Null when no row has a qty or a qty is not
    /// above 0 or above the unshipped qty (the API checks the same, SD-E-145).
    /// </summary>
    public static CancelUnshippedRequest? Request(
        IEnumerable<UnshippedCancelRow> rows, DateOnly cancelDt, IReadOnlyList<bool> reasons, DateTimeOffset updatedAt)
    {
        var picked = rows.Where(r => r.Qty is not null && r.Qty != 0).ToList();
        if (picked.Count == 0 || picked.Any(r => r.Qty < 0 || r.Qty > r.UnshippedQty))
        {
            return null;
        }
        return new CancelUnshippedRequest(cancelDt, [.. reasons], [.. picked.Select(r => new CancelUnshippedLineRequest(r.SalesInvoiceLineId, r.Qty!.Value))], updatedAt);
    }
}

/// <summary>The number formats of the sales invoice line columns with the document decimals.</summary>
public static class SalesInvoiceFormats
{
    public static IReadOnlyDictionary<string, string> Of(SalesOrderDecimals decimals) => new Dictionary<string, string>
    {
        ["dnQty"] = N(decimals.Qty),
        ["shippedQty"] = N(decimals.Qty),
        ["cancelledQty"] = N(decimals.Qty),
        ["unshippedQty"] = N(decimals.Qty),
        ["dnPrc"] = N(decimals.Price),
        ["stdPrc"] = N(decimals.Price),
        ["dnAmt"] = N(decimals.Amount),
    };

    private static string N(int decimals) => "N" + decimals.ToString(CultureInfo.InvariantCulture);
}
