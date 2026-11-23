using System.Globalization;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.Labels;
using QuroErp.Domain.Sd.Customers;
using QuroErp.Domain.Sd.SalesInvoices;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Domain.Sd.Shipments;

/// <summary>
/// PDA shipment (SD-UC-054): the order or BILL_THEN_SHIP invoice number scanned on the PDA and the items scanned on it
/// (MM-BR-921, MM-BR-923). The save is the shipment's, with its checks (MM-BR-921).
/// </summary>
public static class PdaIssueRules
{
    /// <summary>
    /// MM-BR-921, MM-BR-923: a scanned order number must be an order of this system with a line left to ship. A BILL_THEN_SHIP
    /// order ships by its invoice number (SD-BR-174, SD-BR-175). customerId, when given, must be the order's customer.
    /// </summary>
    /// <param name="found">False when no order has this number.</param>
    /// <param name="hasOpenLine">True when a line of the order has qty left to ship.</param>
    /// <exception cref="DomainException">
    /// MM-E-923 for an unknown order or one with nothing left to ship (closed); SD-E-174 for a BILL_THEN_SHIP order; Common-E-006
    /// for an order of another customer.
    /// </exception>
    public static void EnsurePdaOrder(
        string soNo, bool found, string? status, string? billingFlow, long? orderCustomerId, long? customerId, bool hasOpenLine)
    {
        if (!found)
        {
            throw new DomainException(LabelCode.UnknownCode, soNo);
        }
        if (billingFlow == Customer.BillThenShip)
        {
            throw new DomainException(ShipmentRules.BillThenShipOrderCode, soNo);
        }
        if (customerId is not null && orderCustomerId != customerId)
        {
            throw new DomainException(ShipmentRules.NotFoundCode, soNo);
        }
        if ((status ?? SalesOrderStatus.Entered) == SalesOrderStatus.Closed || !hasOpenLine)
        {
            throw new DomainException(LabelCode.UnknownCode, soNo);
        }
    }

    /// <summary>
    /// MM-BR-921, MM-BR-923: a scanned invoice number must be a BILL_THEN_SHIP invoice that is not void with a line left to ship
    /// (SD-BR-175). customerId, when given, must be the invoice's customer.
    /// </summary>
    /// <param name="found">False when no invoice has this number.</param>
    /// <param name="hasOpenLine">True when a line of the invoice has qty left to ship.</param>
    /// <exception cref="DomainException">
    /// MM-E-923 for an unknown invoice or one with nothing left to ship; SD-E-175 for an invoice that is void or not BILL_THEN_SHIP;
    /// Common-E-006 for an invoice of another customer.
    /// </exception>
    public static void EnsurePdaInvoice(
        string dnNo, bool found, string? status, string? billingFlow, long? invoiceCustomerId, long? customerId, bool hasOpenLine)
    {
        if (!found)
        {
            throw new DomainException(LabelCode.UnknownCode, dnNo);
        }
        if (billingFlow != Customer.BillThenShip || status == SalesInvoiceStatus.Void)
        {
            throw new DomainException(ShipmentRules.InvoiceLineCode, dnNo, "-", 0m);
        }
        if (customerId is not null && invoiceCustomerId != customerId)
        {
            throw new DomainException(ShipmentRules.NotFoundCode, dnNo);
        }
        if (!hasOpenLine)
        {
            throw new DomainException(LabelCode.UnknownCode, dnNo);
        }
    }

    /// <summary>MM-BR-921, MM-BR-923: an item scanned on the PDA must have a line left to ship on the scanned order or invoice.</summary>
    /// <param name="itemCd">The item's code for the message; null when no item has this id.</param>
    /// <exception cref="DomainException">MM-E-923 with the item code, or the id when the item is unknown.</exception>
    public static void EnsureItemOnDocument(long itemId, string? itemCd, IEnumerable<long> openLineItemIds)
    {
        if (!openLineItemIds.Contains(itemId))
        {
            throw new DomainException(LabelCode.UnknownCode, itemCd ?? itemId.ToString(CultureInfo.InvariantCulture));
        }
    }
}
