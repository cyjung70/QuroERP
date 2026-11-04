using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.GoodsReceipts;

namespace QuroErp.Domain.Mm.SupplierInvoices;

/// <summary>
/// Header fields of a supplier invoice (MM-BR-751, MM-BR-756). RefNo is the vendor's invoice number; the currency is the matched
/// receipts' and the rate the document currency's value in base currency (BC-BR-412).
/// </summary>
public sealed record SupplierInvoiceHeader(
    long VendorId, string? RefNo, DateOnly InvoiceDt, DateOnly DueDt, string? Terms, string CurrencyCode, decimal ExchangeRate,
    bool TaxInvoiceReceived, string? Remarks);

/// <summary>A new line the user enters: the receipt line it matches and its own quantity, price and tax (MM-BR-753~755).</summary>
public sealed record SupplierInvoiceLineInput(long GoodsReceiptLineId, decimal Qty, decimal Prc, long TaxCodeId, bool Deductible);

/// <summary>A saved line. Saved lines never change; to change one, delete it and add it again (MM-BR-760).</summary>
public sealed record StoredSupplierInvoiceLine(
    long Id, int Seq, long GoodsReceiptLineId, long ItemId, decimal Qty, decimal Prc, long TaxCodeId, bool Deductible, decimal PvAmt);

/// <summary>
/// A goods receipt line as the invoice reads it while its receipt is locked. TaxKind is the kind of the receipt line's tax code
/// and ExchangeRate the receipt's rate, which give the receipt line's stock amount (MM-BR-164). PoPrc and PoDcRate are the PO
/// line's price and discount, null for a receipt line without a PO (MM-BR-754).
/// </summary>
public sealed record InvoiceReceiptLine(
    long Id, string GrNo, int Seq, long VendorId, string CurrencyCode, decimal ExchangeRate, long ItemId, string? ItemType,
    long WarehouseId, long? LotId, decimal GrQty, decimal GrPrc, long TaxCodeId, string TaxKind, decimal SupplyAmt, decimal TaxAmt,
    bool Deductible, decimal InvoicedQty, decimal ReturnedQty, decimal? PoPrc, decimal? PoDcRate)
{
    /// <summary>MM-BR-753: receipt qty − returned qty − net invoiced qty.</summary>
    public decimal OpenQty => GrQty - ReturnedQty - InvoicedQty;
}

/// <summary>
/// A line with the amounts the server computed: supply and tax with the invoice date rates (MM-BR-755) and, for a new line of
/// an inventory item, the price variance in base currency (MM-BR-758). Id is null for a new line.
/// </summary>
public sealed record SupplierInvoiceLine(
    long? Id, int Seq, long GoodsReceiptLineId, long ItemId, decimal Qty, decimal Prc, long TaxCodeId, decimal SupplyAmt,
    decimal TaxAmt, bool Deductible, decimal PvAmt);

/// <summary>Header totals: supply, tax, total = supply + tax (MM-BR-755) and the total in base currency (BC-BR-413).</summary>
public sealed record SupplierInvoiceTotals(decimal SupplyAmt, decimal TaxAmt, decimal TotAmt, decimal TotBaseAmt);

/// <summary>
/// What the rules need: the receipt lines the invoice lines match (new and saved) and the tax codes in use with their rate on
/// the invoice date.
/// </summary>
public sealed record SupplierInvoiceMasters(
    IReadOnlyDictionary<long, InvoiceReceiptLine> ReceiptLines, IReadOnlyDictionary<long, ReceiptTaxCode> TaxCodes);

/// <summary>Company settings for amounts: base currency, tax unit (BC-BR-543) and the MM rounding mode (BC-BR-424).</summary>
public sealed record SupplierInvoiceSettings(string BaseCurrencyCode, string TaxCalcMode, RoundingMode RoundingMode = RoundingMode.HalfUp);

/// <summary>A new line whose price differs from its PO price, or its receipt price without a PO (MM-BR-754).</summary>
public sealed record PriceDifference(int Seq, decimal Prc, decimal ReferencePrice);

/// <summary>Supplier invoice type (mm_supplier_invoice.type).</summary>
public static class SupplierInvoiceType
{
    public const string Invoice = "INV";

    /// <summary>The negative invoice a vendor return makes (MM-BR-762).</summary>
    public const string DebitMemo = "DM";
}

/// <summary>Supplier invoice status: the payment status computed from the amounts (MM-BR-761).</summary>
public static class SupplierInvoiceStatus
{
    /// <summary>Nothing paid.</summary>
    public const string Open = "OP";

    /// <summary>Some amount paid.</summary>
    public const string PartiallyPaid = "PA";

    /// <summary>Balance 0.</summary>
    public const string Closed = "CL";

    /// <summary>Void, or total 0.</summary>
    public const string Void = "VO";
}

/// <summary>
/// Supplier invoice input checked on the server before save (MM-UC-032, docs/business-rules/MM/supplier-invoice.md). Tax comes
/// from TaxCalculator; the price variance movements are posted by the inventory service.
/// </summary>
public static class SupplierInvoiceRules
{
    /// <summary>
    /// No line, or a line from a receipt of another vendor or currency, or a vendor change while lines are saved (MM-BR-751,
    /// MM-BR-756). Arg: the invoice currency.
    /// </summary>
    public const string ReceiptMismatchCode = "MM-E-751";

    /// <summary>A quantity not above 0 or above the receipt line's open qty (MM-BR-753). Args: line, open qty.</summary>
    public const string QtyCode = "MM-E-753";

    /// <summary>A price of 0 (MM-BR-754). Arg: line.</summary>
    public const string PriceCode = "MM-E-754";

    /// <summary>A price that differs from the PO price (MM-BR-754). Args: line, price, PO price.</summary>
    public const string PriceDifferenceWarningCode = "MM-W-754";

    /// <summary>Request field that answers <see cref="PriceDifferenceWarningCode"/> (api.md "경고").</summary>
    public const string ConfirmPriceDifferenceField = "confirmPriceDifference";

    /// <summary>
    /// The invoice is paid, a saved line was changed (MM-BR-760), or the date, currency or rate of an invoice with saved lines
    /// changed (as the goods receipt, MM-BR-117, MM-BR-161). Arg: invoice number.
    /// </summary>
    public const string LockedCode = "MM-E-760";

    /// <summary>A Debit Memo is changed only through its vendor return (MM-BR-762). Arg: invoice number.</summary>
    public const string DebitMemoCode = "MM-E-762";

    /// <summary>The vendor already has an invoice with this number (MM-BR-764). Arg: vendor invoice number.</summary>
    public const string DuplicateRefNoCode = "MM-E-764";

    /// <summary>A vendor, receipt line or tax code does not exist or is not in use. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>Item type whose lines post a price variance; service lines have no stock (MM-BR-008, MM-BR-758).</summary>
    public const string InventoryItemType = "INVENTORY";

    /// <summary>MM-BR-762, MM-BR-760: only an invoice (not a Debit Memo) with nothing paid can be changed or deleted.</summary>
    /// <exception cref="DomainException">MM-E-762, MM-E-760.</exception>
    public static void EnsureEditable(string type, string invoiceNo, decimal clsAmt)
    {
        if (type == SupplierInvoiceType.DebitMemo)
        {
            throw new DomainException(DebitMemoCode, invoiceNo);
        }
        if (clsAmt != 0)
        {
            throw new DomainException(LockedCode, invoiceNo);
        }
    }

    /// <summary>MM-BR-760: a saved line comes back as it was.</summary>
    /// <exception cref="DomainException">MM-E-760.</exception>
    public static void EnsureUnchanged(string invoiceNo, StoredSupplierInvoiceLine stored, SupplierInvoiceLineInput line)
    {
        if (line.GoodsReceiptLineId != stored.GoodsReceiptLineId || line.Qty != stored.Qty || line.Prc != stored.Prc
            || line.TaxCodeId != stored.TaxCodeId || line.Deductible != stored.Deductible)
        {
            throw new DomainException(LockedCode, invoiceNo);
        }
    }

    /// <summary>
    /// MM-BR-760: while the invoice has saved lines, its date, currency and rate stay; their tax and price variance were computed
    /// with them. Delete the lines and save first, as on the goods receipt (MM-BR-117, MM-BR-161).
    /// </summary>
    /// <exception cref="DomainException">MM-E-760.</exception>
    public static void EnsureHeaderChange(string invoiceNo, SupplierInvoiceHeader stored, int storedLineCount, SupplierInvoiceHeader header)
    {
        if (storedLineCount > 0 && (header.InvoiceDt != stored.InvoiceDt || header.CurrencyCode != stored.CurrencyCode
                || header.ExchangeRate != stored.ExchangeRate))
        {
            throw new DomainException(LockedCode, invoiceNo);
        }
    }

    /// <summary>
    /// MM-BR-751, MM-BR-756: the currency of the receipts the new lines match. They must share one currency, which must be the
    /// saved invoice's when it has lines.
    /// </summary>
    /// <param name="storedCurrency">The saved invoice's currency when it has saved lines, else null.</param>
    /// <exception cref="DomainException">MM-E-751 when there is no line or the currencies differ.</exception>
    public static string DocumentCurrency(string? storedCurrency, IEnumerable<InvoiceReceiptLine> addedReceiptLines)
    {
        var currencies = addedReceiptLines.Select(l => l.CurrencyCode).Distinct().ToList();
        if (storedCurrency is not null)
        {
            return currencies.All(c => c == storedCurrency) ? storedCurrency : throw new DomainException(ReceiptMismatchCode, storedCurrency);
        }
        return currencies.Count switch
        {
            1 => currencies[0],
            0 => throw new DomainException(ReceiptMismatchCode, ""),
            _ => throw new DomainException(ReceiptMismatchCode, currencies[0]),
        };
    }

    /// <summary>
    /// Checks the header and the new lines and computes every line's amounts. Saved lines keep their values and price variance;
    /// new lines are numbered from nextSeq in the order given. Tax is computed over the whole document with the invoice date
    /// rates (BC-BR-543, MM-BR-755).
    /// </summary>
    /// <param name="releasedQty">Qty per receipt line that this save's deleted lines give back (MM-BR-753).</param>
    /// <exception cref="DomainException">
    /// MM-E-751, 753, 754; Common-E-005, Common-E-006; BC-E-412 for an unknown currency; BC-E-423 for a price or rate with too
    /// many decimals; SD-E-907 for a rate not above 0, or not 1 in the base currency.
    /// </exception>
    public static (IReadOnlyList<SupplierInvoiceLine> Lines, SupplierInvoiceTotals Totals) Calculate(
        SupplierInvoiceHeader header, IReadOnlyList<StoredSupplierInvoiceLine> kept, IReadOnlyList<SupplierInvoiceLineInput> added,
        int nextSeq, IReadOnlyDictionary<long, decimal> releasedQty, SupplierInvoiceMasters masters, SupplierInvoiceSettings settings,
        IAmountRounder rounder, IDecimalPolicy decimals)
    {
        CheckHeader(header, settings, rounder);
        if (kept.Count + added.Count == 0)
        {
            throw new DomainException(ReceiptMismatchCode, header.CurrencyCode);
        }
        var lines = new List<SupplierInvoiceLine>(kept.Count + added.Count);
        lines.AddRange(kept.Select(l => new SupplierInvoiceLine(
            l.Id, l.Seq, l.GoodsReceiptLineId, l.ItemId, l.Qty, l.Prc, l.TaxCodeId, 0m, 0m, l.Deductible, l.PvAmt)));
        foreach (var (line, seq) in added.Select((l, i) => (l, nextSeq + i)))
        {
            var receipt = CheckLine(line, seq, header, masters, decimals);
            lines.Add(new SupplierInvoiceLine(
                null, seq, line.GoodsReceiptLineId, receipt.ItemId, line.Qty, line.Prc, line.TaxCodeId, 0m, 0m, line.Deductible, 0m));
        }
        EnsureOpenQty(lines, releasedQty, masters.ReceiptLines);

        foreach (var taxCodeId in kept.Select(l => l.TaxCodeId).Where(id => !masters.TaxCodes.ContainsKey(id)))
        {
            throw new DomainException(NotFoundCode, taxCodeId);
        }
        var amountDecimals = rounder.DecimalsOf(header.CurrencyCode);
        var tax = new TaxCalculator(rounder).Calculate(
            [.. lines.Select(l => new TaxLineInput(l.TaxCodeId, Rounding.Round(l.Qty * l.Prc, amountDecimals, settings.RoundingMode)))],
            masters.TaxCodes.ToDictionary(t => t.Key, t => t.Value.Rate), settings.TaxCalcMode, header.CurrencyCode, settings.RoundingMode);
        lines = [.. lines.Select((l, i) => l with
        {
            SupplyAmt = tax.Lines[i].SupplyAmt,
            TaxAmt = tax.Lines[i].TaxAmt,
        })];
        lines = [.. lines.Select(l => l.Id is null
            ? l with { PvAmt = PriceVariance(l, masters.ReceiptLines[l.GoodsReceiptLineId], masters.TaxCodes[l.TaxCodeId].Kind, header, settings, rounder) }
            : l)];
        var totals = new SupplierInvoiceTotals(
            tax.SupplyAmt, tax.TaxAmt, tax.TotalAmt,
            ExchangeRate.BaseAmount(tax.TotalAmt, header.ExchangeRate, settings.BaseCurrencyCode, rounder, settings.RoundingMode));
        return (lines, totals);
    }

    /// <summary>
    /// MM-BR-758: the price variance of an inventory line in base currency = the line's stock amount (MM-BR-164) − the receipt
    /// line's stock amount × (qty ÷ receipt qty), rounded to the base currency. 0 for a service line (no stock).
    /// </summary>
    public static decimal PriceVariance(
        SupplierInvoiceLine line, InvoiceReceiptLine receipt, string taxKind, SupplierInvoiceHeader header, SupplierInvoiceSettings settings,
        IAmountRounder rounder)
    {
        if (receipt.ItemType != InventoryItemType || receipt.GrQty == 0)
        {
            return 0m;
        }
        var invoiced = GoodsReceiptRules.StockAmount(
            line.SupplyAmt, line.TaxAmt, taxKind, line.Deductible, header.ExchangeRate, settings.BaseCurrencyCode, rounder, settings.RoundingMode);
        var received = GoodsReceiptRules.StockAmount(
            receipt.SupplyAmt, receipt.TaxAmt, receipt.TaxKind, receipt.Deductible, receipt.ExchangeRate, settings.BaseCurrencyCode, rounder,
            settings.RoundingMode);
        return rounder.Round(invoiced - (received * line.Qty / receipt.GrQty), settings.BaseCurrencyCode, settings.RoundingMode);
    }

    /// <summary>
    /// MM-BR-754: new lines whose price differs from the PO line's net price (price × (100 − discount %) ÷ 100, rounded to the
    /// unit price decimals of the document currency), or from the receipt price for a receipt line without a PO.
    /// </summary>
    public static IReadOnlyList<PriceDifference> PriceDifferences(
        IReadOnlyList<SupplierInvoiceLine> lines, IReadOnlyDictionary<long, InvoiceReceiptLine> receiptLines, string currencyCode,
        IDecimalPolicy decimals) =>
        [.. lines.Where(l => l.Id is null)
            .Select(l => (Line: l, Reference: ReferencePrice(receiptLines[l.GoodsReceiptLineId], currencyCode, decimals)))
            .Where(x => x.Line.Prc != x.Reference)
            .Select(x => new PriceDifference(x.Line.Seq, x.Line.Prc, x.Reference))];

    /// <summary>MM-BR-754: the price an invoice line is compared with.</summary>
    public static decimal ReferencePrice(InvoiceReceiptLine receipt, string currencyCode, IDecimalPolicy decimals) =>
        receipt.PoPrc is { } poPrc
            ? decimals.RoundUnitPrice(DecimalModule.Mm, currencyCode, poPrc * (100m - (receipt.PoDcRate ?? 0m)) / 100m)
            : receipt.GrPrc;

    /// <summary>MM-BR-755: balance = total − paid.</summary>
    public static decimal BalanceOf(decimal totAmt, decimal clsAmt) => totAmt - clsAmt;

    /// <summary>MM-BR-761: void or total 0 → VO, balance = total → OP, balance 0 → CL, else PA. A Debit Memo follows the same rule.</summary>
    public static string StatusOf(bool isVoid, decimal totAmt, decimal balAmt)
    {
        if (isVoid || totAmt == 0)
        {
            return SupplierInvoiceStatus.Void;
        }
        if (balAmt == totAmt)
        {
            return SupplierInvoiceStatus.Open;
        }
        return balAmt == 0 ? SupplierInvoiceStatus.Closed : SupplierInvoiceStatus.PartiallyPaid;
    }

    private static void CheckHeader(SupplierInvoiceHeader header, SupplierInvoiceSettings settings, IAmountRounder rounder)
    {
        if (header.VendorId <= 0)
        {
            throw new DomainException(RequiredCode, "vendorId");
        }
        if (string.IsNullOrWhiteSpace(header.RefNo))
        {
            throw new DomainException(RequiredCode, "refNo");
        }
        rounder.DecimalsOf(header.CurrencyCode);
        // BC-BR-412: rate above zero and 1 for the base currency
        if (header.ExchangeRate <= 0 || (header.CurrencyCode == settings.BaseCurrencyCode && header.ExchangeRate != ExchangeRate.BaseCurrencyRate))
        {
            throw new DomainException(
                ExchangeRateRules.InvalidCode, header.InvoiceDt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                header.CurrencyCode);
        }
        ExchangeRate.EnsureDecimals(header.ExchangeRate, "exchangeRate");
    }

    private static InvoiceReceiptLine CheckLine(
        SupplierInvoiceLineInput line, int seq, SupplierInvoiceHeader header, SupplierInvoiceMasters masters, IDecimalPolicy decimals)
    {
        var receipt = masters.ReceiptLines.GetValueOrDefault(line.GoodsReceiptLineId)
            ?? throw new DomainException(NotFoundCode, line.GoodsReceiptLineId);
        // MM-BR-751, MM-BR-756: only receipt lines of the invoice's vendor in its currency
        if (receipt.VendorId != header.VendorId || receipt.CurrencyCode != header.CurrencyCode)
        {
            throw new DomainException(ReceiptMismatchCode, header.CurrencyCode);
        }
        if (line.Qty <= 0)
        {
            throw new DomainException(QtyCode, seq, receipt.OpenQty);
        }
        decimals.EnsureQty(DecimalModule.Mm, line.Qty, "qty");
        if (line.Prc == 0)
        {
            throw new DomainException(PriceCode, seq);
        }
        decimals.EnsureUnitPrice(DecimalModule.Mm, header.CurrencyCode, line.Prc, "prc");
        if (!masters.TaxCodes.ContainsKey(line.TaxCodeId))
        {
            throw new DomainException(RequiredCode, "taxCodeId");
        }
        return receipt;
    }

    // MM-BR-753: the new lines of one receipt line, together, take at most its open qty plus what this save's deleted lines give
    // back. Reported at the first new line of that receipt line
    private static void EnsureOpenQty(
        IReadOnlyList<SupplierInvoiceLine> lines, IReadOnlyDictionary<long, decimal> releasedQty, IReadOnlyDictionary<long, InvoiceReceiptLine> receiptLines)
    {
        foreach (var group in lines.Where(l => l.Id is null).GroupBy(l => l.GoodsReceiptLineId))
        {
            var open = receiptLines[group.Key].OpenQty + releasedQty.GetValueOrDefault(group.Key);
            if (group.Sum(l => l.Qty) > open)
            {
                throw new DomainException(QtyCode, group.First().Seq, open);
            }
        }
    }
}
