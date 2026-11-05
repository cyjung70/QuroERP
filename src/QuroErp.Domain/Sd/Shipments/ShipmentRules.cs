using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Domain.Sd.Shipments;

/// <summary>
/// A shipment line as the request sends it, with the defaults applied. A line names at most one source: a sales order line
/// (SD-BR-172), a BILL_THEN_SHIP invoice line (SD-BR-175), or neither for a line entered directly (SD-BR-173).
/// </summary>
public sealed record ShipmentLineInput(
    long? Id, long ItemId, string? ItemName, long? WarehouseId, decimal ShipQty, decimal ShipPrc, long TaxCodeId, long? LotId, string? LotNo,
    long? SalesOrderLineId, long? SalesInvoiceLineId);

/// <summary>A saved shipment line.</summary>
public sealed record StoredShipmentLine(
    long Id, int Seq, long ItemId, string? ItemName, long WarehouseId, decimal ShipQty, decimal ShipPrc, string? DcCd, decimal DcRate,
    long TaxCodeId, long? LotId, decimal BilledQty, long? SalesOrderLineId, long? SalesInvoiceLineId);

/// <summary>A sales order line a shipment line ships, read with its order (locked for the save).</summary>
public sealed record ShipmentOrderLine(
    long Id, long SalesOrderId, string SoNo, int Seq, long CustomerId, string BillingFlow, string CurrencyCode, decimal ExchangeRate,
    long ItemId, string? ItemName, decimal SoQty, decimal ShipQty, decimal SoPrc, string? DcCd, decimal DcRate, long TaxCodeId);

/// <summary>A BILL_THEN_SHIP invoice line a shipment line ships, read with its invoice (locked for the save).</summary>
public sealed record ShipmentInvoiceLine(
    long Id, long SalesInvoiceId, string DnNo, int Seq, long CustomerId, string Status, string BillingFlow, string CurrencyCode,
    decimal ExchangeRate, long ItemId, string? ItemName, decimal DnQty, decimal ShippedQty, decimal CancelledQty, decimal DnPrc, string? DcCd,
    decimal DcRate, long TaxCodeId, long? SalesOrderLineId)
{
    /// <summary>SD-BR-143: invoiced − shipped − cancelled.</summary>
    public decimal UnshippedQty => DnQty - ShippedQty - CancelledQty;
}

/// <summary>
/// A new shipment line with the values the save stores: price, discount and tax code from its source (SD-BR-167, SD-BR-175) or as
/// entered (SD-BR-173), the warehouse (MM-BR-703, the consignment warehouse for BILL_THEN_SHIP, SD-BR-176) and the billed qty.
/// </summary>
public sealed record ShipmentLine(
    int Seq, long ItemId, string? ItemName, long WarehouseId, decimal ShipQty, decimal ShipPrc, string? DcCd, decimal DcRate, long TaxCodeId,
    long? LotId, string? LotNo, decimal BilledQty, long? SalesOrderLineId, long? SalesInvoiceLineId);

/// <summary>
/// Shipment input checked on the server before save (SD-UC-051, docs/business-rules/SD/shipment.md). Stock checks are the
/// inventory service's (MM-BR-001~010, MM-BR-353~362).
/// </summary>
public static class ShipmentRules
{
    /// <summary>Line qty is not above 0 or the price is 0 (SD-BR-161). Arg: line number.</summary>
    public const string QtyPriceCode = "SD-E-161";

    /// <summary>The qty is above what the order line has left to ship (SD-BR-162). Args: so_no, order line number, remaining qty.</summary>
    public const string RemainingCode = "SD-E-162";

    /// <summary>The moving average cost of an issued item is 0 (SD-BR-163, SD-BR-114). Arg: item code.</summary>
    public const string CostCode = "SD-E-114";

    /// <summary>The SHIP_AND_BILL invoice has a receipt or credit applied (SD-BR-104, SD-BR-167). Arg: dn_no.</summary>
    public const string InvoicePaidCode = "SD-E-104";

    /// <summary>A saved line was changed (SD-BR-171). Arg: line number.</summary>
    public const string LineChangedCode = "SD-E-107";

    /// <summary>A SHIP_THEN_BILL line that is billed cannot be deleted, nor its shipment voided (SD-BR-168). Args: ship_no, line number.</summary>
    public const string BilledCode = "SD-E-168";

    /// <summary>The shipment currency differs from the order or invoice currency (SD-BR-170, SD-BR-173). Args: currency, source currency.</summary>
    public const string CurrencyCode = "SD-E-170";

    /// <summary>A BILL_THEN_SHIP order line was sent as an order line (SD-BR-174). Arg: so_no.</summary>
    public const string BillThenShipOrderCode = "SD-E-174";

    /// <summary>
    /// A BILL_THEN_SHIP invoice line over its unshipped qty, not shippable, or mixed with order or direct lines (SD-BR-175). Args:
    /// dn_no, invoice line number, unshipped qty ("-", "-", 0 for mixed lines).
    /// </summary>
    public const string InvoiceLineCode = "SD-E-175";

    /// <summary>The line has no warehouse (MM-BR-703). Arg: line number.</summary>
    public const string WarehouseCode = "MM-E-703";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>The customer, order line or invoice line does not exist or belongs to another customer. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>
    /// SD-BR-174: the billing flow of a new shipment. Invoice lines → BILL_THEN_SHIP; order lines → their order's flow; direct lines
    /// only → the customer's, where BILL_THEN_SHIP becomes SHIP_AND_BILL. A saved shipment keeps its flow.
    /// </summary>
    /// <param name="stored">The saved shipment's flow, or null for a new shipment.</param>
    /// <param name="orderLines">Source lines of the lines this save adds that come from an order.</param>
    /// <param name="addsInvoiceLines">The save adds BILL_THEN_SHIP invoice lines.</param>
    /// <param name="addsDirectLines">The save adds lines entered directly.</param>
    /// <exception cref="DomainException">
    /// SD-E-174 for an order line of a BILL_THEN_SHIP order; SD-E-175 when invoice lines and other lines are mixed; SD-E-035 when
    /// order lines of different billing flows are mixed.
    /// </exception>
    public static string BillingFlowOf(
        string? stored, IReadOnlyList<ShipmentOrderLine> orderLines, bool addsInvoiceLines, bool addsDirectLines, string customerFlow)
    {
        if (orderLines.FirstOrDefault(l => l.BillingFlow == Customer.BillThenShip) is { } billFirst)
        {
            throw new DomainException(BillThenShipOrderCode, billFirst.SoNo);
        }
        var flow = stored
            ?? (addsInvoiceLines ? Customer.BillThenShip
                : orderLines.Count > 0 ? orderLines[0].BillingFlow
                : customerFlow == Customer.BillThenShip ? Customer.ShipAndBill : customerFlow);
        // SD-BR-175: a BILL_THEN_SHIP shipment holds invoice lines only
        if ((flow == Customer.BillThenShip) != addsInvoiceLines && (addsInvoiceLines || orderLines.Count > 0 || addsDirectLines))
        {
            throw new DomainException(InvoiceLineCode, "-", "-", 0m);
        }
        if (addsInvoiceLines && (orderLines.Count > 0 || addsDirectLines))
        {
            throw new DomainException(InvoiceLineCode, "-", "-", 0m);
        }
        if (orderLines.FirstOrDefault(l => l.BillingFlow != flow) is { } other)
        {
            throw new DomainException(SalesOrderEntryRules.BillingFlowCode, other.BillingFlow);
        }
        return flow;
    }

    /// <summary>SD-BR-170, SD-BR-173, SD-BR-175: every order and invoice the lines come from has the shipment's currency.</summary>
    /// <exception cref="DomainException">SD-E-170 with the shipment currency and the first other currency.</exception>
    public static void EnsureCurrency(string currencyCode, IEnumerable<string> sourceCurrencies)
    {
        if (sourceCurrencies.FirstOrDefault(c => c != currencyCode) is { } other)
        {
            throw new DomainException(CurrencyCode, currencyCode, other);
        }
    }

    /// <summary>SD-BR-171: a saved line comes back as it was; the item, warehouse, qty, price, tax code and lot cannot change.</summary>
    /// <param name="input">The request line with the stored values as defaults.</param>
    /// <exception cref="DomainException">SD-E-107 with the line number.</exception>
    public static void EnsureUnchanged(StoredShipmentLine stored, ShipmentLineInput input)
    {
        if (input.ItemId != stored.ItemId || input.WarehouseId != stored.WarehouseId || input.ShipQty != stored.ShipQty
            || input.ShipPrc != stored.ShipPrc || input.TaxCodeId != stored.TaxCodeId || (input.LotId is not null && input.LotId != stored.LotId)
            || input.SalesOrderLineId != stored.SalesOrderLineId || input.SalesInvoiceLineId != stored.SalesInvoiceLineId)
        {
            throw new DomainException(LineChangedCode, stored.Seq);
        }
    }

    /// <summary>SD-BR-168: under SHIP_THEN_BILL a billed line cannot be deleted. Void passes every line.</summary>
    /// <exception cref="DomainException">SD-E-168 with the ship_no and the first billed line number.</exception>
    public static void EnsureNotBilled(string billingFlow, string shipNo, IEnumerable<StoredShipmentLine> lines)
    {
        if (billingFlow == Customer.ShipThenBill && lines.FirstOrDefault(l => l.BilledQty > 0) is { } billed)
        {
            throw new DomainException(BilledCode, shipNo, billed.Seq);
        }
    }

    /// <summary>SD-BR-104, SD-BR-167: the SHIP_AND_BILL invoice has no receipt or credit applied (total = balance).</summary>
    /// <exception cref="DomainException">SD-E-104 with the dn_no.</exception>
    public static void EnsureInvoiceOpen(string dnNo, decimal totDnAmt, decimal balAmt)
    {
        if (totDnAmt != balAmt)
        {
            throw new DomainException(InvoicePaidCode, dnNo);
        }
    }

    /// <summary>SD-BR-163, SD-BR-114: an issue at a moving average cost of 0 cannot be saved.</summary>
    /// <exception cref="DomainException">SD-E-114 with the item code.</exception>
    public static void EnsureCost(decimal cost, string itemCd)
    {
        if (cost == 0)
        {
            throw new DomainException(CostCode, itemCd);
        }
    }

    /// <summary>
    /// Checks the new lines and gives them their stored values. Order lines take the order line's price, discount and tax code
    /// (SD-BR-167); invoice lines the invoice line's and the consignment warehouse (SD-BR-175, SD-BR-176); direct lines keep their
    /// price with no discount (SD-BR-173). SHIP_AND_BILL and BILL_THEN_SHIP lines are billed when saved (SD-BR-167, SD-BR-176).
    /// </summary>
    /// <param name="firstSeq">Line number of the first new line.</param>
    /// <param name="itemIds">Items that exist.</param>
    /// <param name="usableWarehouseIds">Warehouses in use among the line warehouses.</param>
    /// <param name="taxCodeIds">Tax codes in use.</param>
    /// <param name="consignmentWarehouseId">The consignment warehouse, required for BILL_THEN_SHIP lines.</param>
    /// <exception cref="DomainException">
    /// SD-E-161, SD-E-004 for an unknown item, MM-E-703 without a warehouse, Common-E-006 for a warehouse not in use, Common-E-005 without a tax code, Common-E-006 for a
    /// source line of another customer or one that does not exist.
    /// </exception>
    public static IReadOnlyList<ShipmentLine> Lines(
        IReadOnlyList<ShipmentLineInput> added, int firstSeq, string billingFlow, long customerId,
        IReadOnlyDictionary<long, ShipmentOrderLine> orderLines, IReadOnlyDictionary<long, ShipmentInvoiceLine> invoiceLines,
        IReadOnlySet<long> itemIds, IReadOnlySet<long> usableWarehouseIds, IReadOnlySet<long> taxCodeIds, long? consignmentWarehouseId)
    {
        var lines = new List<ShipmentLine>(added.Count);
        foreach (var (input, i) in added.Select((l, i) => (l, i)))
        {
            var seq = firstSeq + i;
            var line = input.SalesInvoiceLineId is { } invoiceLineId
                ? FromInvoice(input, seq, Source(invoiceLines, invoiceLineId, customerId, l => l.CustomerId), consignmentWarehouseId)
                : input.SalesOrderLineId is { } orderLineId
                    ? FromOrder(input, seq, Source(orderLines, orderLineId, customerId, l => l.CustomerId))
                    : new ShipmentLine(
                        seq, input.ItemId, input.ItemName, input.WarehouseId ?? 0, input.ShipQty, input.ShipPrc, null, 0m, input.TaxCodeId,
                        input.LotId, input.LotNo, 0m, null, null);
            if (!itemIds.Contains(line.ItemId))
            {
                throw new DomainException(SalesOrderEntryRules.ItemCode, seq);
            }
            if (line.ShipQty <= 0 || line.ShipPrc == 0)
            {
                throw new DomainException(QtyPriceCode, seq);
            }
            if (line.WarehouseId <= 0)
            {
                throw new DomainException(WarehouseCode, seq);
            }
            // MM-BR-704: new lines take warehouses in use only
            if (!usableWarehouseIds.Contains(line.WarehouseId))
            {
                throw new DomainException(NotFoundCode, line.WarehouseId);
            }
            if (!taxCodeIds.Contains(line.TaxCodeId))
            {
                throw new DomainException(RequiredCode, "taxCodeId");
            }
            lines.Add(billingFlow == Customer.ShipThenBill ? line : line with { BilledQty = line.ShipQty });
        }
        return lines;
    }

    /// <summary>
    /// SD-BR-162: per order line, the qty the save adds is not above what is left to ship. The lines this save deletes give their
    /// qty back first. Invoice lines are limited by SD-BR-175 instead.
    /// </summary>
    /// <exception cref="DomainException">SD-E-162 with the so_no, order line number and remaining qty.</exception>
    public static void EnsureRemaining(
        IEnumerable<ShipmentLine> added, IEnumerable<StoredShipmentLine> deleted, IReadOnlyDictionary<long, ShipmentOrderLine> orderLines)
    {
        var givenBack = deleted.Where(l => l.SalesOrderLineId is not null && l.SalesInvoiceLineId is null)
            .GroupBy(l => l.SalesOrderLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.ShipQty));
        foreach (var group in added.Where(l => l.SalesOrderLineId is not null && l.SalesInvoiceLineId is null).GroupBy(l => l.SalesOrderLineId!.Value))
        {
            var order = orderLines[group.Key];
            var remaining = order.SoQty - order.ShipQty + givenBack.GetValueOrDefault(group.Key);
            if (group.Sum(l => l.ShipQty) > remaining)
            {
                throw new DomainException(RemainingCode, order.SoNo, order.Seq, remaining);
            }
        }
    }

    /// <summary>
    /// SD-BR-175: per invoice line, the qty the save adds is not above its unshipped qty, and the invoice is a BILL_THEN_SHIP invoice
    /// that is not void. The lines this save deletes give their qty back first.
    /// </summary>
    /// <exception cref="DomainException">SD-E-175 with the dn_no, invoice line number and unshipped qty.</exception>
    public static void EnsureUnshipped(
        IEnumerable<ShipmentLine> added, IEnumerable<StoredShipmentLine> deleted, IReadOnlyDictionary<long, ShipmentInvoiceLine> invoiceLines,
        string voidStatus)
    {
        var givenBack = deleted.Where(l => l.SalesInvoiceLineId is not null)
            .GroupBy(l => l.SalesInvoiceLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.ShipQty));
        foreach (var group in added.Where(l => l.SalesInvoiceLineId is not null).GroupBy(l => l.SalesInvoiceLineId!.Value))
        {
            var invoice = invoiceLines[group.Key];
            var unshipped = invoice.UnshippedQty + givenBack.GetValueOrDefault(group.Key);
            if (invoice.Status == voidStatus || invoice.BillingFlow != Customer.BillThenShip || group.Sum(l => l.ShipQty) > unshipped)
            {
                throw new DomainException(InvoiceLineCode, invoice.DnNo, invoice.Seq, unshipped);
            }
        }
    }

    private static T Source<T>(IReadOnlyDictionary<long, T> lines, long id, long customerId, Func<T, long> customerOf) =>
        lines.TryGetValue(id, out var line) && customerOf(line) == customerId ? line : throw new DomainException(NotFoundCode, id);

    private static ShipmentLine FromOrder(ShipmentLineInput input, int seq, ShipmentOrderLine order) => new(
        seq, order.ItemId, input.ItemName ?? order.ItemName, input.WarehouseId ?? 0, input.ShipQty, order.SoPrc, order.DcCd, order.DcRate,
        order.TaxCodeId, input.LotId, input.LotNo, 0m, order.Id, null);

    // SD-BR-175: the consignment warehouse whatever the request says; the order line comes from the invoice line (SD-BR-176)
    private static ShipmentLine FromInvoice(ShipmentLineInput input, int seq, ShipmentInvoiceLine invoice, long? consignmentWarehouseId) => new(
        seq, invoice.ItemId, input.ItemName ?? invoice.ItemName, consignmentWarehouseId ?? 0, input.ShipQty, invoice.DnPrc, invoice.DcCd,
        invoice.DcRate, invoice.TaxCodeId, input.LotId, input.LotNo, 0m, invoice.SalesOrderLineId, invoice.Id);
}
