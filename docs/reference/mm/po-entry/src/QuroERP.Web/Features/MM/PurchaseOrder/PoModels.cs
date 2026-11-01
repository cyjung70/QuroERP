// QuroERP · MM · Purchase Order (PO Entry) — view models
// TO BE from AS IS Procentro m_po_hdr / m_po_dtl (po.xml, po.jrxml).
namespace QuroERP.Web.Features.MM.PurchaseOrder;

/// <summary>PO status. Stored as a short code in m_po_hdr.status.</summary>
public enum PoStatus
{
    Draft,              // DR  saved, editable, deletable
    Approved,           // AP  sent to vendor, editable until first receipt
    PartiallyReceived,  // PR  some quantity received: no edit, no delete
    Received,           // RC  all quantity received
    Closed,             // CL  closed manually, no further receipts
    Cancelled           // CN  cancelled before any receipt
}

/// <summary>Row state used by QuroGrid and the save payload.</summary>
public enum RowState { Unchanged, Added, Modified, MarkedForDelete }

/// <summary>Decimal places per currency, read from MM configuration.</summary>
public sealed record CurrencyPrecision(string Currency, int Qty = 2, int Price = 4, int Amount = 2);

public sealed class PoHeader
{
    public string? PoNo { get; set; }                 // auto-numbered by the API on first save
    public PoStatus Status { get; set; } = PoStatus.Draft;
    public DateOnly PoDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string VendorCode { get; set; } = "";       // key is the code, not the name (AS IS used v_name as key)
    public string VendorName { get; set; } = "";
    public string Currency { get; set; } = "KRW";
    public decimal ExchangeRate { get; set; } = 1m;
    public DateOnly? DueDate { get; set; }
    public string? WarehouseCode { get; set; }         // default for new lines
    public string? WarehouseName { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Incoterms { get; set; }             // FOB moved here from the AS IS price column
    public string? TaxCode { get; set; }               // default for new lines
    public string? Buyer { get; set; }
    public string? VendorMessage { get; set; }         // AS IS remarks, max 200
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime? ChangedAt { get; set; }
    public long RowVersion { get; set; }               // optimistic concurrency (xmin in PostgreSQL)
}

public sealed class PoLine
{
    public int Seq { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";         // read-only, resolved from item master
    public string Unit { get; set; } = "";             // read-only, from item master
    public string? WarehouseCode { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }             // was "F.O.B" in AS IS
    public decimal DiscountRate { get; set; }          // percent, 0–100
    public string? TaxCode { get; set; }
    public decimal TaxRate { get; set; }               // percent, resolved from TaxCode
    public DateOnly? DueDate { get; set; }
    public decimal ReceivedQty { get; set; }           // read-only, from goods receipts

    // calculated (see PoCalculator)
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }

    public RowState State { get; set; } = RowState.Added;

    /// <summary>Snapshot taken when the row was loaded; Undo Row restores it.</summary>
    public PoLine? Original { get; set; }

    public PoLine Clone() => (PoLine)MemberwiseClone();
}

public sealed record PoTotals(decimal Subtotal, decimal Discount, decimal Net, decimal Tax, decimal Total, decimal TotalLocal);

public sealed class PoDocument
{
    public PoHeader Header { get; set; } = new();
    public List<PoLine> Lines { get; set; } = new();
}
