using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Inventory;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Domain.Sd.SalesInvoices;

/// <summary>
/// An invoice line as the request sends it. ShipmentLineId bills a SHIP_THEN_BILL shipment line (SD-BR-115), SalesOrderLineId a
/// BILL_THEN_SHIP order line (SD-BR-141); neither is a line entered directly (SD-BR-116). Null values take their defaults.
/// </summary>
public sealed record SalesInvoiceLineInput(
    long? Id, long ItemId, string? ItemName, decimal DnQty, decimal? DnPrc, string? DcCd, long? TaxCodeId, long? WarehouseId, long? LotId,
    string? LotNo, long? ShipmentLineId, long? SalesOrderLineId);

/// <summary>A saved invoice line with its shipped and cancelled qty (SD-BR-143).</summary>
public sealed record StoredInvoiceLine(
    long Id, int Seq, long ItemId, string? ItemName, decimal DnQty, decimal DnPrc, string? DcCd, decimal DcRate, long TaxCodeId,
    long WarehouseId, long? LotId, long? ShipmentLineId, long? SalesOrderLineId, decimal ShippedQty, decimal CancelledQty)
{
    /// <summary>SD-BR-143: invoiced − shipped − cancelled.</summary>
    public decimal UnshippedQty => DnQty - ShippedQty - CancelledQty;
}

/// <summary>A shipment line an invoice line bills, read with its shipment (locked for the save).</summary>
public sealed record InvoiceShipmentSource(
    long Id, long ShipmentId, string ShipNo, int Seq, long CustomerId, string Status, string BillingFlow, string CurrencyCode,
    decimal ExchangeRate, long ItemId, string? ItemName, long WarehouseId, decimal ShipQty, decimal BilledQty, decimal ShipPrc, string? DcCd,
    decimal DcRate, long TaxCodeId, long? SalesOrderLineId)
{
    public decimal UnbilledQty => ShipQty - BilledQty;
}

/// <summary>A BILL_THEN_SHIP order line an invoice line bills, read with its order (locked for the save).</summary>
public sealed record InvoiceOrderSource(
    long Id, long SalesOrderId, string SoNo, int Seq, long CustomerId, string Status, string BillingFlow, string CurrencyCode,
    decimal ExchangeRate, long ItemId, string? ItemName, decimal SoQty, decimal DnQty, decimal SoPrc, string? DcCd, decimal DcRate,
    long TaxCodeId)
{
    /// <summary>SD-BR-141: order qty − billed qty.</summary>
    public decimal RemainingQty => SoQty - DnQty;
}

/// <summary>A new invoice line with the values the save stores.</summary>
public sealed record SalesInvoiceLine(
    int Seq, long ItemId, string? ItemName, decimal DnQty, decimal DnPrc, string? DcCd, decimal DcRate, long TaxCodeId, long WarehouseId,
    long? LotId, string? LotNo, long? ShipmentLineId, long? SalesOrderLineId);

/// <summary>
/// What the line checks need from masters: item types and codes of the items that exist, discount rates, tax codes in use, the
/// warehouses in use among the line warehouses, and the default tax code of a direct line by item (SD-BR-033, BC-BR-525).
/// </summary>
public sealed record SalesInvoiceMasters(
    IReadOnlyDictionary<long, string> ItemTypes, IReadOnlyDictionary<long, string> ItemCodes, IReadOnlyDictionary<string, decimal> DiscountRates,
    IReadOnlySet<long> TaxCodeIds, IReadOnlySet<long> UsableWarehouseIds, IReadOnlyDictionary<long, long> DefaultTaxCodeIds);

/// <summary>One line of an unshipped cancellation (SD-BR-145) as the request sends it.</summary>
public sealed record UnshippedCancelInput(long SalesInvoiceLineId, decimal Qty);

/// <summary>
/// Sales invoice input checked on the server before save (SD-UC-003, docs/business-rules/SD/sales-invoice.md). Amounts are
/// <see cref="Shipments.ShipmentInvoice"/>'s; stock checks are the inventory service's.
/// </summary>
public static class SalesInvoiceRules
{
    /// <summary>Line qty is not above 0 (SD-BR-101). Arg: line number.</summary>
    public const string QtyCode = "SD-E-101";

    /// <summary>
    /// The qty is above the shipment line's unbilled qty, or the shipment is not a SHIP_THEN_BILL shipment in use (SD-BR-102,
    /// SD-BR-115). Args: ship_no, shipment line number, unbilled qty.
    /// </summary>
    public const string UnbilledCode = "SD-E-102";

    /// <summary>Line unit price is 0 (SD-BR-103). Arg: line number.</summary>
    public const string PriceCode = "SD-E-103";

    /// <summary>The invoice has a receipt or credit applied (SD-BR-104, SD-BR-261). Arg: dn_no.</summary>
    public const string PaidCode = "SD-E-104";

    /// <summary>A saved line was changed (SD-BR-107). Arg: line number.</summary>
    public const string LineChangedCode = "SD-E-107";

    /// <summary>The invoice was made by a SHIP_AND_BILL shipment, which voids it (SD-BR-113, SD-BR-169). Arg: dn_no.</summary>
    public const string MadeByShipmentCode = "SD-E-113";

    /// <summary>A direct line of a stock-managed item (SD-BR-116). Arg: item code.</summary>
    public const string StockItemCode = "SD-E-116";

    /// <summary>The customer of a saved invoice cannot change (SD-BR-119). No args.</summary>
    public const string CustomerLockedCode = "SD-E-119";

    /// <summary>The invoice currency differs from a shipment or order it bills (SD-BR-131). Args: currency, source currency.</summary>
    public const string CurrencyCode = "SD-E-131";

    /// <summary>The tax invoice cannot be printed: no taxable line, or the customer is not a business with its details (SD-BR-134). Arg: dn_no.</summary>
    public const string TaxInvoiceCode = "SD-E-134";

    /// <summary>A line of the SHIP_AND_BILL shipment changes on the invoice, or a shipment or order line is added to it (SD-BR-136). Arg: line number.</summary>
    public const string ShipmentLineCode = "SD-E-136";

    /// <summary>
    /// A BILL_THEN_SHIP order line over its remaining qty, of another flow or a closed order, or mixed with shipment lines
    /// (SD-BR-141). Args: so_no, order line number, remaining qty ("-", "-", 0 for mixed lines).
    /// </summary>
    public const string BillFirstCode = "SD-E-141";

    /// <summary>A BILL_THEN_SHIP line with shipped or cancelled qty cannot be deleted, nor its invoice voided (SD-BR-144). Args: dn_no, line number.</summary>
    public const string ShippedCode = "SD-E-144";

    /// <summary>
    /// A cancellation qty not above 0 or above the line's unshipped qty, or an invoice that is not a BILL_THEN_SHIP invoice in use
    /// (SD-BR-145). Args: dn_no, line number, unshipped qty.
    /// </summary>
    public const string CancelCode = "SD-E-145";

    /// <summary>The Credit Memo of a cancellation is applied, so the cancellation stays (SD-BR-146). Arg: cm_no.</summary>
    public const string CreditAppliedCode = "SD-E-146";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>The customer, warehouse or a source line does not exist or belongs to another customer. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>Number of Credit Memo reasons (cm_reason01..08, SD-BR-208).</summary>
    public const int ReasonCount = 8;

    /// <summary>SD-BR-119: a saved invoice keeps its customer.</summary>
    /// <exception cref="DomainException">SD-E-119 when the request names another customer.</exception>
    public static void EnsureSameCustomer(long storedCustomerId, long requestCustomerId)
    {
        if (storedCustomerId != requestCustomerId)
        {
            throw new DomainException(CustomerLockedCode);
        }
    }

    /// <summary>SD-BR-104, SD-BR-261: no receipt or credit applied yet (total = balance).</summary>
    /// <exception cref="DomainException">SD-E-104 with the dn_no.</exception>
    public static void EnsureOpen(string dnNo, decimal totDnAmt, decimal balAmt)
    {
        if (totDnAmt != balAmt)
        {
            throw new DomainException(PaidCode, dnNo);
        }
    }

    /// <summary>
    /// SD-BR-115, SD-BR-136, SD-BR-141: the flow an invoice belongs to after the save. The invoice a SHIP_AND_BILL shipment made
    /// stays SHIP_AND_BILL; an invoice with BILL_THEN_SHIP order lines is BILL_THEN_SHIP; any other is SHIP_THEN_BILL.
    /// </summary>
    /// <param name="storedFlow">The saved invoice's flow, or null for a new one.</param>
    /// <param name="hasShipmentLines">Kept or added lines bill shipment lines.</param>
    /// <param name="hasOrderLines">Kept or added lines bill BILL_THEN_SHIP order lines.</param>
    /// <exception cref="DomainException">SD-E-141 when shipment lines and BILL_THEN_SHIP order lines are mixed.</exception>
    public static string BillingFlowOf(string? storedFlow, bool hasShipmentLines, bool hasOrderLines)
    {
        if (hasShipmentLines && hasOrderLines)
        {
            throw new DomainException(BillFirstCode, "-", "-", 0m);
        }
        return storedFlow == Customer.ShipAndBill ? Customer.ShipAndBill
            : hasOrderLines ? Customer.BillThenShip
            : Customer.ShipThenBill;
    }

    /// <summary>SD-BR-131: every shipment and order the lines bill has the invoice's currency.</summary>
    /// <exception cref="DomainException">SD-E-131 with the invoice currency and the first other currency.</exception>
    public static void EnsureCurrency(string currencyCode, IEnumerable<string> sourceCurrencies)
    {
        if (sourceCurrencies.FirstOrDefault(c => c != currencyCode) is { } other)
        {
            throw new DomainException(CurrencyCode, currencyCode, other);
        }
    }

    /// <summary>
    /// SD-BR-107: a saved line comes back as it was. The item, qty, price, discount, tax code, warehouse and source cannot change;
    /// a null value in the request means "as saved".
    /// </summary>
    /// <exception cref="DomainException">SD-E-107 with the line number.</exception>
    public static void EnsureUnchanged(StoredInvoiceLine stored, SalesInvoiceLineInput input)
    {
        if (input.ItemId != stored.ItemId || input.DnQty != stored.DnQty || (input.DnPrc is { } prc && prc != stored.DnPrc)
            || (input.DcCd is { } dcCd && dcCd != (stored.DcCd ?? ""))
            || (input.TaxCodeId is { } taxCodeId && taxCodeId != stored.TaxCodeId)
            || (input.WarehouseId is { } warehouseId && warehouseId != stored.WarehouseId)
            || (input.ShipmentLineId is { } shipmentLineId && shipmentLineId != stored.ShipmentLineId)
            || (input.SalesOrderLineId is { } orderLineId && orderLineId != stored.SalesOrderLineId))
        {
            throw new DomainException(LineChangedCode, stored.Seq);
        }
    }

    /// <summary>
    /// SD-BR-136, SD-BR-144: the lines this save deletes. A line the SHIP_AND_BILL shipment made is deleted on the shipment only;
    /// a BILL_THEN_SHIP line only while nothing of it is shipped or cancelled.
    /// </summary>
    /// <exception cref="DomainException">SD-E-136 with the line number; SD-E-144 with the dn_no and line number.</exception>
    public static void EnsureDeletable(string dnNo, string billingFlow, IEnumerable<StoredInvoiceLine> deleted)
    {
        foreach (var line in deleted)
        {
            if (billingFlow == Customer.ShipAndBill && line.ShipmentLineId is not null)
            {
                throw new DomainException(ShipmentLineCode, line.Seq);
            }
            if (line.ShippedQty != 0 || line.CancelledQty != 0)
            {
                throw new DomainException(ShippedCode, dnNo, line.Seq);
            }
        }
    }

    /// <summary>SD-BR-104, SD-BR-113, SD-BR-144: the invoice can be voided here.</summary>
    /// <exception cref="DomainException">SD-E-104, SD-E-113 for an invoice of a SHIP_AND_BILL shipment, SD-E-144.</exception>
    public static void EnsureVoidable(string dnNo, string billingFlow, decimal totDnAmt, decimal balAmt, IEnumerable<StoredInvoiceLine> lines)
    {
        EnsureOpen(dnNo, totDnAmt, balAmt);
        if (billingFlow == Customer.ShipAndBill)
        {
            throw new DomainException(MadeByShipmentCode, dnNo);
        }
        if (lines.FirstOrDefault(l => l.ShippedQty != 0 || l.CancelledQty != 0) is { } shipped)
        {
            throw new DomainException(ShippedCode, dnNo, shipped.Seq);
        }
    }

    /// <summary>
    /// Checks the new lines and gives them their stored values. A shipment line takes the shipment line's item, price, discount,
    /// warehouse and order line (SD-BR-115, SD-BR-406); an order line the order line's item, price and discount and the header
    /// warehouse unless the line names one (SD-BR-141); a direct line its own price and discount and must not keep stock
    /// (SD-BR-116). The tax code defaults to the source's and can be changed (SD-BR-132).
    /// </summary>
    /// <param name="firstSeq">Line number of the first new line.</param>
    /// <param name="madeByShipment">The invoice of a SHIP_AND_BILL shipment: only direct lines are added (SD-BR-136).</param>
    /// <exception cref="DomainException">
    /// SD-E-101, 102, 103, 116, 136, 141, 404; SD-E-004 for an unknown item; MM-E-703 without a warehouse; Common-E-005 without a
    /// tax code; Common-E-006 for a warehouse not in use or a source line of another customer or that does not exist.
    /// </exception>
    public static IReadOnlyList<SalesInvoiceLine> Lines(
        IReadOnlyList<SalesInvoiceLineInput> added, int firstSeq, bool madeByShipment, long customerId, long headerWarehouseId,
        IReadOnlyDictionary<long, InvoiceShipmentSource> shipmentLines, IReadOnlyDictionary<long, InvoiceOrderSource> orderLines,
        SalesInvoiceMasters masters)
    {
        var lines = new List<SalesInvoiceLine>(added.Count);
        foreach (var (input, i) in added.Select((l, i) => (l, i)))
        {
            var seq = firstSeq + i;
            if (madeByShipment && (input.ShipmentLineId is not null || input.SalesOrderLineId is not null))
            {
                throw new DomainException(ShipmentLineCode, seq);
            }
            var line = input.ShipmentLineId is { } shipmentLineId
                ? FromShipment(input, seq, Source(shipmentLines, shipmentLineId, customerId, s => s.CustomerId))
                : input.SalesOrderLineId is { } orderLineId
                    ? FromOrder(input, seq, Source(orderLines, orderLineId, customerId, o => o.CustomerId), headerWarehouseId)
                    : Direct(input, seq, headerWarehouseId, masters);
            if (!masters.ItemTypes.ContainsKey(line.ItemId))
            {
                throw new DomainException(SalesOrderEntryRules.ItemCode, seq);
            }
            if (line.DnQty <= 0)
            {
                throw new DomainException(QtyCode, seq);
            }
            if (line.DnPrc == 0)
            {
                throw new DomainException(PriceCode, seq);
            }
            if (line.WarehouseId <= 0)
            {
                throw new DomainException(Shipments.ShipmentRules.WarehouseCode, seq);
            }
            if (!masters.UsableWarehouseIds.Contains(line.WarehouseId))
            {
                throw new DomainException(NotFoundCode, line.WarehouseId);
            }
            if (!masters.TaxCodeIds.Contains(line.TaxCodeId))
            {
                throw new DomainException(RequiredCode, "taxCodeId");
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// SD-BR-102: per shipment line, the qty the save adds is not above its unbilled qty, and the shipment is a SHIP_THEN_BILL
    /// shipment that is not void. The lines this save deletes give their qty back first.
    /// </summary>
    /// <exception cref="DomainException">SD-E-102 with the ship_no, shipment line number and unbilled qty.</exception>
    public static void EnsureUnbilled(
        IEnumerable<SalesInvoiceLine> added, IEnumerable<StoredInvoiceLine> deleted, IReadOnlyDictionary<long, InvoiceShipmentSource> shipmentLines,
        string voidStatus)
    {
        var givenBack = deleted.Where(l => l.ShipmentLineId is not null)
            .GroupBy(l => l.ShipmentLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.DnQty));
        foreach (var group in added.Where(l => l.ShipmentLineId is not null).GroupBy(l => l.ShipmentLineId!.Value))
        {
            var source = shipmentLines[group.Key];
            var unbilled = source.UnbilledQty + givenBack.GetValueOrDefault(group.Key);
            if (source.Status == voidStatus || source.BillingFlow != Customer.ShipThenBill || group.Sum(l => l.DnQty) > unbilled)
            {
                throw new DomainException(UnbilledCode, source.ShipNo, source.Seq, unbilled);
            }
        }
    }

    /// <summary>
    /// SD-BR-141: per order line, the qty the save adds is not above what is left to bill, and the order is a BILL_THEN_SHIP order
    /// that is not closed. The lines this save deletes give their qty back first.
    /// </summary>
    /// <exception cref="DomainException">SD-E-141 with the so_no, order line number and remaining qty.</exception>
    public static void EnsureRemaining(
        IEnumerable<SalesInvoiceLine> added, IEnumerable<StoredInvoiceLine> deleted, IReadOnlyDictionary<long, InvoiceOrderSource> orderLines,
        string closedStatus)
    {
        var givenBack = deleted.Where(l => l.SalesOrderLineId is not null && l.ShipmentLineId is null)
            .GroupBy(l => l.SalesOrderLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.DnQty));
        foreach (var group in added.Where(l => l.SalesOrderLineId is not null && l.ShipmentLineId is null).GroupBy(l => l.SalesOrderLineId!.Value))
        {
            var source = orderLines[group.Key];
            var remaining = source.RemainingQty + givenBack.GetValueOrDefault(group.Key);
            if (source.Status == closedStatus || source.BillingFlow != Customer.BillThenShip || group.Sum(l => l.DnQty) > remaining)
            {
                throw new DomainException(BillFirstCode, source.SoNo, source.Seq, remaining);
            }
        }
    }

    /// <summary>
    /// SD-BR-145: the lines and qty of an unshipped cancellation. The invoice is a BILL_THEN_SHIP invoice in use; each qty is above
    /// 0 and, per line, not above its unshipped qty.
    /// </summary>
    /// <returns>The cancelled lines with their qty, in line order.</returns>
    /// <exception cref="DomainException">
    /// SD-E-145 with the dn_no, line number and unshipped qty; Common-E-005 without lines; Common-E-006 for a line of another invoice.
    /// </exception>
    public static IReadOnlyList<(StoredInvoiceLine Line, decimal Qty)> CancelLines(
        string dnNo, string billingFlow, string status, IReadOnlyList<StoredInvoiceLine> lines, IReadOnlyList<UnshippedCancelInput> requested)
    {
        if (requested.Count == 0)
        {
            throw new DomainException(RequiredCode, "lines");
        }
        if (billingFlow != Customer.BillThenShip || status == SalesInvoiceStatus.Void)
        {
            throw new DomainException(CancelCode, dnNo, "-", 0m);
        }
        var byId = lines.ToDictionary(l => l.Id);
        var result = new List<(StoredInvoiceLine Line, decimal Qty)>();
        foreach (var group in requested.GroupBy(r => r.SalesInvoiceLineId))
        {
            var line = byId.GetValueOrDefault(group.Key) ?? throw new DomainException(NotFoundCode, group.Key);
            var qty = group.Sum(r => r.Qty);
            if (group.Any(r => r.Qty <= 0) || qty > line.UnshippedQty)
            {
                throw new DomainException(CancelCode, dnNo, line.Seq, line.UnshippedQty);
            }
            result.Add((line, qty));
        }
        return [.. result.OrderBy(r => r.Line.Seq)];
    }

    /// <summary>SD-BR-145: the Credit Memo price of a cancelled line is the invoice line price after its discount.</summary>
    public static decimal NetPrice(decimal dnPrc, decimal dcRate) => dnPrc * (1m + dcRate / 100m);

    /// <summary>SD-BR-146: the Credit Memo of a cancellation is not applied to an invoice or receipt (total = balance).</summary>
    /// <exception cref="DomainException">SD-E-146 with the cm_no.</exception>
    public static void EnsureCreditNotApplied(string cmNo, decimal totDnAmt, decimal balAmt)
    {
        if (totDnAmt != balAmt)
        {
            throw new DomainException(CreditAppliedCode, cmNo);
        }
    }

    /// <summary>
    /// SD-BR-134: a KR tax invoice needs a taxable line (VAT_STANDARD, VAT_ZERO) and a business customer with its business number,
    /// name and CEO name.
    /// </summary>
    /// <exception cref="DomainException">SD-E-134 with the dn_no.</exception>
    public static void EnsureTaxInvoice(
        string dnNo, IEnumerable<string> lineTaxKinds, string? partyType, string? businessRegNo, string? customerName, string? ceoName)
    {
        var taxable = lineTaxKinds.Any(k => k is TaxKind.VatStandard or TaxKind.VatZero);
        if (!taxable || partyType != Customer.PartyBusiness || string.IsNullOrWhiteSpace(businessRegNo) || string.IsNullOrWhiteSpace(customerName)
            || string.IsNullOrWhiteSpace(ceoName))
        {
            throw new DomainException(TaxInvoiceCode, dnNo);
        }
    }

    private static T Source<T>(IReadOnlyDictionary<long, T> lines, long id, long customerId, Func<T, long> customerOf) =>
        lines.TryGetValue(id, out var line) && customerOf(line) == customerId ? line : throw new DomainException(NotFoundCode, id);

    private static SalesInvoiceLine FromShipment(SalesInvoiceLineInput input, int seq, InvoiceShipmentSource source) => new(
        seq, source.ItemId, input.ItemName ?? source.ItemName, input.DnQty, source.ShipPrc, source.DcCd, source.DcRate,
        input.TaxCodeId ?? source.TaxCodeId, source.WarehouseId, null, null, source.Id, source.SalesOrderLineId);

    private static SalesInvoiceLine FromOrder(SalesInvoiceLineInput input, int seq, InvoiceOrderSource source, long headerWarehouseId) => new(
        seq, source.ItemId, input.ItemName ?? source.ItemName, input.DnQty, source.SoPrc, source.DcCd, source.DcRate,
        input.TaxCodeId ?? source.TaxCodeId, input.WarehouseId ?? headerWarehouseId, input.LotId, input.LotNo, null, source.Id);

    // SD-BR-116: only items that keep no stock (MM-BR-008); SD-BR-404: the discount code's rate
    private static SalesInvoiceLine Direct(SalesInvoiceLineInput input, int seq, long headerWarehouseId, SalesInvoiceMasters masters)
    {
        if (masters.ItemTypes.TryGetValue(input.ItemId, out var type) && type != ItemStock.Service)
        {
            throw new DomainException(
                StockItemCode, masters.ItemCodes.GetValueOrDefault(input.ItemId) ?? input.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        decimal dcRate = 0;
        var dcCd = string.IsNullOrEmpty(input.DcCd) ? null : input.DcCd;
        if (dcCd is { } code && !masters.DiscountRates.TryGetValue(code, out dcRate))
        {
            throw new DomainException(SalesOrderEntryRules.DiscountCode, seq, code);
        }
        return new SalesInvoiceLine(
            seq, input.ItemId, input.ItemName, input.DnQty, input.DnPrc ?? 0m, dcCd, dcRate, input.TaxCodeId ?? masters.DefaultTaxCodeIds.GetValueOrDefault(input.ItemId),
            input.WarehouseId ?? headerWarehouseId, null, null, null, null);
    }
}
