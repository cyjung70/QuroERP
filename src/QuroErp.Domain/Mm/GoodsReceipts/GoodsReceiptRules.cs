using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.SetItems;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Mm.PurchaseOrders;

namespace QuroErp.Domain.Mm.GoodsReceipts;

/// <summary>
/// Header fields of a goods receipt. WarehouseId is the line default (MM-BR-703); the rate is the document currency's value in
/// base currency (BC-BR-412).
/// </summary>
public sealed record GoodsReceiptHeader(
    long VendorId, DateOnly GrDt, long WarehouseId, string? RefNo, string? Remarks, string CurrencyCode, decimal ExchangeRate);

/// <summary>A new line the user enters, with the header warehouse and vendor tax code already applied as defaults.</summary>
public sealed record GoodsReceiptLineInput(
    long ItemId, string? ItemName, long? WarehouseId, long? LotId, string? LotNo, decimal GrQty, decimal GrPrc, long TaxCodeId,
    bool Deductible, long? PurchaseOrderLineId);

/// <summary>A saved line. Saved lines never change (MM-BR-107); InvoicedQty and ReturnedQty lock the receipt (MM-BR-104).</summary>
public sealed record StoredGoodsReceiptLine(
    long Id, int Seq, long ItemId, string? ItemName, long WarehouseId, long? LotId, decimal GrQty, decimal GrPrc, long TaxCodeId,
    bool Deductible, long? PurchaseOrderLineId, decimal InvoicedQty, decimal ReturnedQty);

/// <summary>
/// A line with the amounts the server computed: GrAmt = round(qty × price) (MM-BR-112), supply and tax (MM-BR-162) and, for new
/// lines, the stock amount in base currency (MM-BR-164). Id is null for a new line.
/// </summary>
public sealed record GoodsReceiptLine(
    long? Id, int Seq, long ItemId, string? ItemName, long WarehouseId, long? LotId, string? LotNo, decimal GrQty, decimal GrPrc,
    decimal GrAmt, long TaxCodeId, decimal SupplyAmt, decimal TaxAmt, bool Deductible, long? PurchaseOrderLineId, decimal StockAmt)
{
    /// <summary>Unit cost in base currency for the GR stock movement: stock amount / qty (MM-BR-111, MM-BR-164).</summary>
    public decimal StockPrice => StockAmt / GrQty;
}

/// <summary>Header totals: supply, tax, total = supply + tax (MM-BR-112) and the total in base currency (BC-BR-413).</summary>
public sealed record GoodsReceiptTotals(decimal SupplyAmt, decimal TaxAmt, decimal TotGrAmt, decimal TotBaseAmt);

/// <summary>A tax code's rate on the receipt date and its kind, which decides whether the tax goes into cost (MM-BR-164).</summary>
public sealed record ReceiptTaxCode(TaxCodeRate Rate, string Kind);

/// <summary>
/// A purchase order line a receipt line can come from, read while its order is locked. ReceivedQty is what every saved receipt
/// line has received (mm_purchase_order_line.receipt_qty, MM-BR-108).
/// </summary>
public sealed record ReceiptPoLine(
    long Id, long PurchaseOrderId, string PoNo, int PoSeq, string Status, long VendorId, string CurrencyCode, decimal ExchangeRate,
    decimal PoQty, decimal ReceivedQty);

/// <summary>What the rules need from masters: item types, usable warehouses, tax codes and the PO lines the new lines name.</summary>
public sealed record GoodsReceiptMasters(
    IReadOnlyDictionary<long, string> ItemTypes, IReadOnlySet<long> UsableWarehouses, IReadOnlyDictionary<long, ReceiptTaxCode> TaxCodes,
    IReadOnlyDictionary<long, ReceiptPoLine> PoLines);

/// <summary>Company settings for amounts: base currency, tax unit (BC-BR-543) and the MM rounding mode (BC-BR-424).</summary>
public sealed record GoodsReceiptSettings(string BaseCurrencyCode, string TaxCalcMode, RoundingMode RoundingMode = RoundingMode.HalfUp);

/// <summary>A new line that receives more than its PO line has left (MM-BR-102).</summary>
public sealed record OverReceipt(string PoNo, int Seq, decimal RemainingQty);

/// <summary>Values of the over-receipt mode (mm_inventory_setting.over_receipt_mode, MM-BR-102). No row means DENY.</summary>
public static class OverReceiptModes
{
    public const string Deny = "DENY";
    public const string Warn = "WARN";

    public static IReadOnlyList<string> All { get; } = [Deny, Warn];

    public static string OrDefault(string? mode) => mode == Warn ? Warn : Deny;
}

/// <summary>Goods receipt status: the invoice matching status computed from quantities (MM-BR-105).</summary>
public static class GoodsReceiptStatus
{
    /// <summary>Nothing invoiced.</summary>
    public const string Open = "OP";

    /// <summary>Some quantity invoiced.</summary>
    public const string PartiallyInvoiced = "PA";

    /// <summary>Every line fully invoiced (uninvoiced qty 0).</summary>
    public const string Closed = "CL";

    /// <summary>Total 0.</summary>
    public const string Void = "VO";
}

/// <summary>
/// Goods receipt input checked on the server before save (MM-UC-004, docs/business-rules/MM/goods-receipt.md). Tax comes from
/// TaxCalculator; stock movements are posted by the inventory service.
/// </summary>
public static class GoodsReceiptRules
{
    public const string QtyCode = "MM-E-101";

    /// <summary>More than the PO line has left in DENY mode (MM-BR-102). Args: PO number, line, remaining qty.</summary>
    public const string OverReceiptCode = "MM-E-102";

    /// <summary>More than the PO line has left in WARN mode (MM-BR-102). Args: PO number, line, remaining qty.</summary>
    public const string OverReceiptWarningCode = "MM-W-102";

    /// <summary>Request field that answers <see cref="OverReceiptWarningCode"/> (api.md "경고").</summary>
    public const string ConfirmOverReceiptField = "confirmOverReceipt";

    public const string PriceCode = "MM-E-103";

    /// <summary>A line is invoiced or returned (MM-BR-104, MM-BR-790). Arg: GR number.</summary>
    public const string LockedCode = "MM-E-104";

    /// <summary>A saved line was changed (MM-BR-107). Arg: line.</summary>
    public const string LineChangedCode = "MM-E-107";

    /// <summary>The item is missing or not Inventory or Service (MM-BR-115). Arg: line.</summary>
    public const string ItemCode = "MM-E-115";

    /// <summary>The vendor of a saved receipt changed (MM-BR-116).</summary>
    public const string VendorChangedCode = "MM-E-116";

    /// <summary>The date of a receipt with lines changed (MM-BR-117).</summary>
    public const string DateChangedCode = "MM-E-117";

    /// <summary>The currency or rate differs from the PO, or the currency of a receipt with lines changed (MM-BR-161). Args: currency, rate.</summary>
    public const string CurrencyCode = "MM-E-161";

    /// <summary>A line comes from a PO that is not PO/RE or belongs to another vendor (MM-BR-024, MM-BR-113). Arg: PO number.</summary>
    public const string PoNotReceivableCode = "MM-E-024";

    /// <summary>A line has no warehouse (MM-BR-703). Arg: line.</summary>
    public const string WarehouseCode = "MM-E-703";

    /// <summary>A vendor, warehouse, tax code or PO line does not exist or is not in use. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>Item types a receipt can take (MM-BR-115). Assets are Enterprise.</summary>
    public static IReadOnlyList<string> ReceivableItemTypes { get; } = ["INVENTORY", "SERVICE"];

    /// <summary>MM-BR-104, MM-BR-790: no line matched by a supplier invoice and no vendor return.</summary>
    /// <exception cref="DomainException">MM-E-104.</exception>
    public static void EnsureEditable(string grNo, IEnumerable<StoredGoodsReceiptLine> lines)
    {
        if (lines.Any(l => l.InvoicedQty != 0 || l.ReturnedQty != 0))
        {
            throw new DomainException(LockedCode, grNo);
        }
    }

    /// <summary>
    /// MM-BR-116: the vendor is fixed after the first save. MM-BR-117: no date change while lines are saved. MM-BR-161: no
    /// currency change while lines are saved.
    /// </summary>
    /// <exception cref="DomainException">MM-E-116, MM-E-117, MM-E-161.</exception>
    public static void EnsureHeaderChange(GoodsReceiptHeader stored, int storedLineCount, GoodsReceiptHeader header)
    {
        if (header.VendorId != stored.VendorId)
        {
            throw new DomainException(VendorChangedCode);
        }
        if (storedLineCount > 0 && header.GrDt != stored.GrDt)
        {
            throw new DomainException(DateChangedCode);
        }
        if (storedLineCount > 0 && (header.CurrencyCode != stored.CurrencyCode || header.ExchangeRate != stored.ExchangeRate))
        {
            throw new DomainException(CurrencyCode, header.CurrencyCode, header.ExchangeRate);
        }
    }

    /// <summary>MM-BR-107: a saved line comes back as it was; to change it, delete it and add a new one.</summary>
    /// <exception cref="DomainException">MM-E-107.</exception>
    public static void EnsureUnchanged(StoredGoodsReceiptLine stored, GoodsReceiptLineInput line)
    {
        if (line.ItemId != stored.ItemId || line.GrQty != stored.GrQty || line.GrPrc != stored.GrPrc
            || (line.WarehouseId ?? stored.WarehouseId) != stored.WarehouseId || line.TaxCodeId != stored.TaxCodeId
            || line.Deductible != stored.Deductible || line.PurchaseOrderLineId != stored.PurchaseOrderLineId
            || (line.LotId is { } lotId && lotId != stored.LotId))
        {
            throw new DomainException(LineChangedCode, stored.Seq);
        }
    }

    /// <summary>
    /// MM-BR-161: the currency and rate of a receipt with PO lines are the PO's; without PO lines the requested ones (the vendor's
    /// currency and the date's rate by default). All PO lines must share one currency and rate.
    /// </summary>
    /// <exception cref="DomainException">MM-E-161 when the request or another PO line disagrees.</exception>
    public static (string CurrencyCode, decimal ExchangeRate) DocumentCurrency(
        string? requestedCurrency, decimal? requestedRate, IEnumerable<ReceiptPoLine> poLines)
    {
        var pairs = poLines.Select(p => (p.CurrencyCode, p.ExchangeRate)).Distinct().ToList();
        if (pairs.Count == 0)
        {
            return (requestedCurrency ?? "", requestedRate ?? 0m);
        }
        var (currency, rate) = pairs[0];
        if (pairs.Count > 1 || (requestedCurrency is not null && requestedCurrency != currency)
            || (requestedRate is not null && requestedRate != rate))
        {
            throw new DomainException(CurrencyCode, requestedCurrency ?? currency, requestedRate ?? rate);
        }
        return (currency, rate);
    }

    /// <summary>
    /// Checks the header and the new lines and computes every line's amounts. Saved lines keep their values and are not checked
    /// again; new lines are numbered from nextSeq in the order given. Tax is computed over the whole document (BC-BR-543).
    /// </summary>
    /// <param name="storedWarehouseId">The saved header warehouse, which may stay after it is set not in use (MM-BR-704).</param>
    /// <exception cref="DomainException">
    /// MM-E-101, 103, 115, 703, MM-E-024, MM-E-161; Common-E-005, Common-E-006; BC-E-412 for an unknown currency; BC-E-423 for a
    /// rate with more than 10 decimals.
    /// </exception>
    public static (IReadOnlyList<GoodsReceiptLine> Lines, GoodsReceiptTotals Totals) Calculate(
        GoodsReceiptHeader header, IReadOnlyList<StoredGoodsReceiptLine> kept, IReadOnlyList<GoodsReceiptLineInput> added, int nextSeq,
        GoodsReceiptMasters masters, GoodsReceiptSettings settings, IAmountRounder rounder, long? storedWarehouseId = null)
    {
        CheckHeader(header, storedWarehouseId, masters, settings, rounder);
        var lines = new List<GoodsReceiptLine>(kept.Count + added.Count);
        lines.AddRange(kept.Select(l => new GoodsReceiptLine(
            l.Id, l.Seq, l.ItemId, l.ItemName, l.WarehouseId, l.LotId, null, l.GrQty, l.GrPrc, 0m, l.TaxCodeId, 0m, 0m, l.Deductible,
            l.PurchaseOrderLineId, 0m)));
        foreach (var (line, seq) in added.Select((l, i) => (l, nextSeq + i)))
        {
            CheckLine(line, seq, header, masters);
            lines.Add(new GoodsReceiptLine(
                null, seq, line.ItemId, line.ItemName, line.WarehouseId!.Value, line.LotId, line.LotNo, line.GrQty, line.GrPrc, 0m,
                line.TaxCodeId, 0m, 0m, line.Deductible, line.PurchaseOrderLineId, 0m));
        }

        foreach (var taxCodeId in kept.Select(l => l.TaxCodeId).Where(id => !masters.TaxCodes.ContainsKey(id)))
        {
            throw new DomainException(NotFoundCode, taxCodeId);
        }
        // MM-BR-112: supply = round(qty × price); MM-BR-162: tax with the receipt date rates
        lines = [.. lines.Select(l => l with { GrAmt = Rounding.Round(l.GrQty * l.GrPrc, rounder.DecimalsOf(header.CurrencyCode), settings.RoundingMode) })];
        var tax = new TaxCalculator(rounder).Calculate(
            [.. lines.Select(l => new TaxLineInput(l.TaxCodeId, l.GrAmt))], masters.TaxCodes.ToDictionary(t => t.Key, t => t.Value.Rate),
            settings.TaxCalcMode, header.CurrencyCode, settings.RoundingMode);
        lines = [.. lines.Select((l, i) => l with
        {
            SupplyAmt = tax.Lines[i].SupplyAmt,
            TaxAmt = tax.Lines[i].TaxAmt,
            StockAmt = l.Id is null
                ? StockAmount(tax.Lines[i].SupplyAmt, tax.Lines[i].TaxAmt, masters.TaxCodes[l.TaxCodeId].Kind, l.Deductible, header, settings, rounder)
                : 0m,
        })];
        var totals = new GoodsReceiptTotals(
            tax.SupplyAmt, tax.TaxAmt, tax.TotalAmt,
            ExchangeRate.BaseAmount(tax.TotalAmt, header.ExchangeRate, settings.BaseCurrencyCode, rounder, settings.RoundingMode));
        return (lines, totals);
    }

    /// <summary>
    /// MM-BR-164: stock amount in base currency = supply + the tax that goes into cost (US sales tax, KR non-deductible VAT),
    /// converted with the document rate. KR deductible VAT stays out of cost.
    /// </summary>
    public static decimal StockAmount(
        decimal supplyAmt, decimal taxAmt, string taxKind, bool deductible, GoodsReceiptHeader header, GoodsReceiptSettings settings,
        IAmountRounder rounder) =>
        StockAmount(supplyAmt, taxAmt, taxKind, deductible, header.ExchangeRate, settings.BaseCurrencyCode, rounder, settings.RoundingMode);

    /// <summary>MM-BR-164 with the document rate given: the supplier invoice uses it for its lines and the receipt lines (MM-BR-758).</summary>
    public static decimal StockAmount(
        decimal supplyAmt, decimal taxAmt, string taxKind, bool deductible, decimal exchangeRate, string baseCurrencyCode,
        IAmountRounder rounder, RoundingMode roundingMode)
    {
        var costTax = taxKind == TaxKind.SalesTax || (IsVat(taxKind) && !deductible) ? taxAmt : 0m;
        return ExchangeRate.BaseAmount(supplyAmt + costTax, exchangeRate, baseCurrencyCode, rounder, roundingMode);
    }

    /// <summary>
    /// MM-BR-102: new lines that, together, receive more than their PO line has left (PO qty − what saved receipt lines received).
    /// One entry per PO line, at its first new line. Lines without a PO are not checked (MM-BR-114).
    /// </summary>
    public static IReadOnlyList<OverReceipt> OverReceipts(IReadOnlyList<GoodsReceiptLine> lines, IReadOnlyDictionary<long, ReceiptPoLine> poLines) =>
        [.. lines.Where(l => l.Id is null && l.PurchaseOrderLineId is not null)
            .GroupBy(l => l.PurchaseOrderLineId!.Value)
            .Select(g => (Po: poLines[g.Key], First: g.First(), Qty: g.Sum(l => l.GrQty)))
            .Where(x => x.Qty > x.Po.PoQty - x.Po.ReceivedQty)
            .Select(x => new OverReceipt(x.Po.PoNo, x.First.Seq, x.Po.PoQty - x.Po.ReceivedQty))];

    /// <summary>
    /// MM-BR-105: total 0 → VO, nothing invoiced → OP, every line's uninvoiced qty (receipt − returned − invoiced, MM-BR-753) 0
    /// → CL, else PA.
    /// </summary>
    public static string StatusOf(decimal totGrAmt, IReadOnlyCollection<(decimal GrQty, decimal InvoicedQty, decimal ReturnedQty)> lines) =>
        StatusOf(totGrAmt, lines.Any(l => l.InvoicedQty != 0), lines.All(l => l.GrQty - l.ReturnedQty - l.InvoicedQty == 0));

    /// <summary>MM-BR-105 from flags the list query computes: any line invoiced, every line's uninvoiced qty 0.</summary>
    public static string StatusOf(decimal totGrAmt, bool anyInvoiced, bool allInvoiced)
    {
        if (totGrAmt == 0)
        {
            return GoodsReceiptStatus.Void;
        }
        if (!anyInvoiced)
        {
            return GoodsReceiptStatus.Open;
        }
        return allInvoiced ? GoodsReceiptStatus.Closed : GoodsReceiptStatus.PartiallyInvoiced;
    }

    private static bool IsVat(string kind) => kind is TaxKind.VatStandard or TaxKind.VatZero or TaxKind.VatExempt;

    private static void CheckHeader(
        GoodsReceiptHeader header, long? storedWarehouseId, GoodsReceiptMasters masters, GoodsReceiptSettings settings, IAmountRounder rounder)
    {
        if (header.VendorId <= 0)
        {
            throw new DomainException(RequiredCode, nameof(GoodsReceiptHeader.VendorId));
        }
        rounder.DecimalsOf(header.CurrencyCode);
        // BC-BR-412: rate above zero and 1 for the base currency
        if (header.ExchangeRate <= 0 || (header.CurrencyCode == settings.BaseCurrencyCode && header.ExchangeRate != 1m))
        {
            throw new DomainException(CurrencyCode, header.CurrencyCode, header.ExchangeRate);
        }
        ExchangeRate.EnsureDecimals(header.ExchangeRate, "exchangeRate");
        if (header.WarehouseId != storedWarehouseId && !masters.UsableWarehouses.Contains(header.WarehouseId))
        {
            throw new DomainException(NotFoundCode, header.WarehouseId);
        }
    }

    private static void CheckLine(GoodsReceiptLineInput line, int seq, GoodsReceiptHeader header, GoodsReceiptMasters masters)
    {
        masters.ItemTypes.TryGetValue(line.ItemId, out var itemType);
        SetItemRules.EnsureNotReceived(itemType, seq);
        if (itemType is null || !ReceivableItemTypes.Contains(itemType))
        {
            throw new DomainException(ItemCode, seq);
        }
        if (line.GrQty <= 0)
        {
            throw new DomainException(QtyCode, seq);
        }
        if (line.GrPrc == 0)
        {
            throw new DomainException(PriceCode, seq);
        }
        // MM-BR-703: the line warehouse, the header's by default; MM-BR-704: only warehouses in use for new lines
        if (line.WarehouseId is not { } warehouseId)
        {
            throw new DomainException(WarehouseCode, seq);
        }
        if (!masters.UsableWarehouses.Contains(warehouseId))
        {
            throw new DomainException(NotFoundCode, warehouseId);
        }
        if (!masters.TaxCodes.ContainsKey(line.TaxCodeId))
        {
            throw new DomainException(RequiredCode, nameof(GoodsReceiptLineInput.TaxCodeId));
        }
        if (line.PurchaseOrderLineId is { } poLineId)
        {
            var po = masters.PoLines.GetValueOrDefault(poLineId) ?? throw new DomainException(NotFoundCode, poLineId);
            // MM-BR-024, MM-BR-113: only approved or partly received orders of the same vendor
            if (po.Status is not (PoStatusCode.Approved or PoStatusCode.PartiallyReceived) || po.VendorId != header.VendorId)
            {
                throw new DomainException(PoNotReceivableCode, po.PoNo);
            }
        }
    }
}
