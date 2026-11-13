namespace QuroErp.Domain.Sd.SalesInvoices;

/// <summary>sd_sales_invoice.type: invoices and Credit Memos share the table, and each API reads its own type only (SD-BR-120).</summary>
public static class SalesInvoiceTypes
{
    public const string Invoice = "IV";
    public const string CreditMemo = "CM";
}

/// <summary>Invoice status (SA00, SD-BR-105): OP not paid, PA partly paid, CL paid, VO void.</summary>
public static class SalesInvoiceStatus
{
    public const string Open = "OP";
    public const string PartlyPaid = "PA";
    public const string Closed = "CL";
    public const string Void = "VO";

    /// <summary>SD-BR-105, SD-BR-204: void → VO, balance = total → OP, balance 0 → CL, otherwise PA.</summary>
    public static string Of(bool isVoid, decimal totDnAmt, decimal balAmt) =>
        isVoid ? Void : balAmt == totDnAmt ? Open : balAmt == 0 ? Closed : PartlyPaid;
}
