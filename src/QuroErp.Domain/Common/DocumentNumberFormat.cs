using System.Globalization;

namespace QuroErp.Domain.Common;

/// <summary>
/// How a document type is numbered (SD-BR-006): prefix + yymm of the document date + a sequence that restarts
/// every month, or prefix + one sequence that never restarts. sys_document_number keeps one counter per
/// (<see cref="Prefix"/>, <see cref="Period"/>).
/// </summary>
public sealed record DocumentNumberFormat(string Prefix, int Digits, bool RestartsMonthly)
{
    /// <summary>The sequence has no more digits. Numbers are never reused (SD-BR-006).</summary>
    public const string Exhausted = "SD-E-006";

    /// <summary>SD-BR-006: SO + yymm + 5 digits.</summary>
    public static readonly DocumentNumberFormat SalesOrder = new("SO", 5, RestartsMonthly: true);

    /// <summary>SD-BR-106: DN + 7 digits, no month.</summary>
    public static readonly DocumentNumberFormat SalesInvoice = new("DN", 7, RestartsMonthly: false);

    /// <summary>SD-BR-206: CM + yymm + 5 digits.</summary>
    public static readonly DocumentNumberFormat CreditMemo = new("CM", 5, RestartsMonthly: true);

    /// <summary>MM-BR-017: PO + yymm + 5 digits.</summary>
    public static readonly DocumentNumberFormat PurchaseOrder = new("PO", 5, RestartsMonthly: true);

    /// <summary>MM-BR-106: GR + yymm + 5 digits.</summary>
    public static readonly DocumentNumberFormat GoodsReceipt = new("GR", 5, RestartsMonthly: true);

    /// <summary>MM-BR-208: UR + yymm + 5 digits.</summary>
    public static readonly DocumentNumberFormat UnplannedReceipt = new("UR", 5, RestartsMonthly: true);

    /// <summary>MM-BR-308: CT + yymm + 5 digits.</summary>
    public static readonly DocumentNumberFormat StockCount = new("CT", 5, RestartsMonthly: true);

    /// <summary>MM-BR-607: stock movements (mm_goods_movement.document_no), ID + yymm + 10 digits.</summary>
    public static readonly DocumentNumberFormat GoodsMovement = new("ID", 10, RestartsMonthly: true);

    /// <summary>MM-BR-708: stock transfers, TR + yymm + 10 digits.</summary>
    public static readonly DocumentNumberFormat StockTransfer = new("TR", 10, RestartsMonthly: true);

    /// <summary>The sys_document_number.period of a document dated documentDt: yymm, or "" for continuous numbers.</summary>
    public string Period(DateOnly documentDt) =>
        RestartsMonthly ? documentDt.ToString("yyMM", CultureInfo.InvariantCulture) : "";

    /// <param name="lastNo">The last number issued in this period, or null when none was issued yet.</param>
    /// <returns>The number after lastNo, or sequence 1 when lastNo is null or not in this format.</returns>
    /// <exception cref="DomainException">SD-E-006 when the sequence has no more digits.</exception>
    public string Next(DateOnly documentDt, string? lastNo)
    {
        var head = Prefix + Period(documentDt);
        long sequence = 1;
        if (lastNo is not null && lastNo.Length == head.Length + Digits && lastNo.StartsWith(head, StringComparison.Ordinal)
            && long.TryParse(lastNo.AsSpan(head.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var last))
        {
            sequence = last + 1;
        }
        var text = sequence.ToString(CultureInfo.InvariantCulture);
        if (text.Length > Digits)
        {
            throw new DomainException(Exhausted, head);
        }
        return head + text.PadLeft(Digits, '0');
    }
}
