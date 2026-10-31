using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Domain.Mm.PurchaseOrders;

/// <summary>Header fields the user enters. Rate is the document currency's value in base currency (BC-BR-412).</summary>
public sealed record PurchaseOrderHeader(
    long VendorId, DateOnly PoDt, string CurrencyCode, decimal ExchangeRate, string? Incoterms, DateOnly? DueDt, long? WarehouseId,
    string? PaymentTerms, string? Buyer, string? Remarks);

/// <summary>A line the user enters, with the header defaults already applied (MM-BR-026). DcRate is a percentage.</summary>
public sealed record PurchaseOrderLineInput(
    long? Id, long ItemId, string? ItemName, decimal PoQty, decimal PoPrc, decimal DcRate, DateOnly? DueDt, long? WarehouseId, long TaxCodeId);

/// <summary>A line with the amounts the server computed (MM-BR-015, MM-BR-032). PoAmt is the supply amount before tax.</summary>
public sealed record PurchaseOrderLine(
    long? Id, int Seq, long ItemId, string? ItemName, decimal PoQty, decimal PoPrc, decimal GrossAmt, decimal DcRate, decimal DcAmt,
    decimal PoAmt, long TaxCodeId, decimal SupplyAmt, decimal TaxAmt, DateOnly? DueDt, long? WarehouseId);

/// <summary>Header totals: gross and discount for the totals panel (MM-BR-025), supply, tax and total (BC-BR-541).</summary>
public sealed record PurchaseOrderTotals(decimal GrossAmt, decimal DcAmt, decimal SupplyAmt, decimal TaxAmt, decimal TotPoAmt, decimal TotBaseAmt);

/// <summary>What the entry rules need from masters: item types, usable warehouses and tax code rates.</summary>
public sealed record PurchaseOrderMasters(
    IReadOnlyDictionary<long, string> ItemTypes, IReadOnlySet<long> UsableWarehouses, IReadOnlyDictionary<long, TaxCodeRate> TaxRates);

/// <summary>
/// Company settings for amounts: base currency, tax unit (BC-BR-543) and the MM module rounding mode, which rounds every
/// calculated amount and the tax (BC-BR-424, BC-BR-545).
/// </summary>
public sealed record PurchaseOrderSettings(string BaseCurrencyCode, string TaxCalcMode, RoundingMode RoundingMode = RoundingMode.HalfUp);

/// <summary>
/// Purchase order input checked on the server before save (MM-UC-003, docs/business-rules/MM/purchase-order.md). Line amounts
/// come from PoCalculator and tax from TaxCalculator; status rules are PoStatusRules.
/// </summary>
public static class PoEntryRules
{
    public const string QtyCode = "MM-E-011";
    public const string PriceCode = "MM-E-012";
    public const string ItemCode = "MM-E-014";
    public const string DiscountRateCode = "MM-E-025";
    public const string DueDateCode = "MM-E-026";
    public const string IncotermsCode = "MM-E-027";
    public const string CurrencyCode = "MM-E-031";

    /// <summary>A vendor, warehouse or other master row does not exist or is not in use. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>Incoterms 2020 (MM-BR-027).</summary>
    public static IReadOnlyList<string> IncotermsCodes { get; } = ["EXW", "FCA", "FAS", "FOB", "CFR", "CIF", "CPT", "CIP", "DAP", "DPU", "DDP"];

    /// <summary>Item types a purchase order line can buy (MM-BR-014).</summary>
    public static IReadOnlyList<string> PurchasableItemTypes { get; } = ["INVENTORY", "SERVICE"];

    /// <summary>
    /// Checks the header and lines and computes every amount. Lines are numbered 1..n in the order given.
    /// </summary>
    /// <param name="previousCurrencyCode">Currency of the stored order when it already has lines (MM-BR-031), else null.</param>
    /// <exception cref="DomainException">MM-E-011, 012, 014, 025, 026, 027, 031; Common-E-005; BC-E-412 for an unknown currency.</exception>
    public static (IReadOnlyList<PurchaseOrderLine> Lines, PurchaseOrderTotals Totals) Calculate(
        PurchaseOrderHeader header, IReadOnlyList<PurchaseOrderLineInput> lines, string? previousCurrencyCode,
        PurchaseOrderMasters masters, PurchaseOrderSettings settings, IAmountRounder rounder)
    {
        CheckHeader(header, previousCurrencyCode, masters, settings, rounder);
        var decimals = rounder.DecimalsOf(header.CurrencyCode);
        var poLines = new List<PoLine>(lines.Count);
        var computed = new List<PurchaseOrderLine>(lines.Count);
        foreach (var (line, seq) in lines.Select((l, i) => (l, i + 1)))
        {
            CheckLine(line, seq, header, masters);
            var poLine = new PoLine(seq, line.PoQty, line.PoPrc, line.DcRate);
            var amounts = PoCalculator.CalcLine(poLine, decimals, settings.RoundingMode);
            poLines.Add(poLine);
            computed.Add(new PurchaseOrderLine(
                line.Id, seq, line.ItemId, line.ItemName, line.PoQty, line.PoPrc, amounts.Gross, line.DcRate, amounts.Discount,
                amounts.Net, line.TaxCodeId, 0m, 0m, line.DueDt, line.WarehouseId));
        }

        // MM-BR-032: tax of the supply amounts, as an estimate (BC-BR-547)
        var tax = new TaxCalculator(rounder).Calculate(
            [.. computed.Select(l => new TaxLineInput(l.TaxCodeId, l.PoAmt))], masters.TaxRates, settings.TaxCalcMode, header.CurrencyCode,
            settings.RoundingMode);
        computed = [.. computed.Select((l, i) => l with { SupplyAmt = tax.Lines[i].SupplyAmt, TaxAmt = tax.Lines[i].TaxAmt })];
        var sums = PoCalculator.CalcTotals(poLines, decimals, settings.RoundingMode);
        var totals = new PurchaseOrderTotals(
            sums.Subtotal, sums.Discount, tax.SupplyAmt, tax.TaxAmt, tax.TotalAmt,
            rounder.Round(tax.TotalAmt * header.ExchangeRate, settings.BaseCurrencyCode, settings.RoundingMode));
        return (computed, totals);
    }

    /// <summary>Saving an approved order keeps it approved and counts a revision; drafts stay at 0 (MM-BR-021).</summary>
    public static int RevisionAfterSave(PoStatus status, int revisionNo) => status == PoStatus.Approved ? revisionNo + 1 : revisionNo;

    private static void CheckHeader(
        PurchaseOrderHeader header, string? previousCurrencyCode, PurchaseOrderMasters masters, PurchaseOrderSettings settings,
        IAmountRounder rounder)
    {
        if (header.VendorId <= 0)
        {
            throw new DomainException(RequiredCode, nameof(PurchaseOrderHeader.VendorId));
        }
        rounder.DecimalsOf(header.CurrencyCode);
        // MM-BR-031: rate above zero, 1 for the base currency, and no currency change once lines exist
        if (header.ExchangeRate <= 0
            || (header.CurrencyCode == settings.BaseCurrencyCode && header.ExchangeRate != 1m)
            || (previousCurrencyCode is not null && previousCurrencyCode != header.CurrencyCode))
        {
            throw new DomainException(CurrencyCode, header.CurrencyCode, header.ExchangeRate);
        }
        if (header.Incoterms is not null && !IncotermsCodes.Contains(header.Incoterms))
        {
            throw new DomainException(IncotermsCode, header.Incoterms);
        }
        if (header.DueDt < header.PoDt)
        {
            throw new DomainException(DueDateCode, 0, header.DueDt!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        CheckWarehouse(header.WarehouseId, masters);
    }

    private static void CheckLine(PurchaseOrderLineInput line, int seq, PurchaseOrderHeader header, PurchaseOrderMasters masters)
    {
        if (!masters.ItemTypes.TryGetValue(line.ItemId, out var itemType) || !PurchasableItemTypes.Contains(itemType))
        {
            throw new DomainException(ItemCode, seq);
        }
        if (line.PoQty <= 0)
        {
            throw new DomainException(QtyCode, seq);
        }
        if (line.PoPrc == 0)
        {
            throw new DomainException(PriceCode, seq);
        }
        if (line.DcRate is < 0 or > 100)
        {
            throw new DomainException(DiscountRateCode, seq);
        }
        if (line.DueDt < header.PoDt)
        {
            throw new DomainException(DueDateCode, seq, line.DueDt!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        CheckWarehouse(line.WarehouseId, masters);
        if (!masters.TaxRates.ContainsKey(line.TaxCodeId))
        {
            throw new DomainException(RequiredCode, nameof(PurchaseOrderLineInput.TaxCodeId));
        }
    }

    // MM-BR-026: a warehouse in use
    private static void CheckWarehouse(long? warehouseId, PurchaseOrderMasters masters)
    {
        if (warehouseId is { } id && !masters.UsableWarehouses.Contains(id))
        {
            throw new DomainException(NotFoundCode, id);
        }
    }
}
