using System.Globalization;
using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.Customers;

namespace QuroErp.Domain.Sd.SalesOrders;

/// <summary>
/// Ship-to and freight of an order or customer: the fields SD-BR-009 compares and copies back to the customer.
/// </summary>
public sealed record SalesOrderShipTo(
    string? ShipName, string? ShipAddr1, string? ShipAddr2, string? ShipCity, string? ShipState, string? ShipZipCode, string? ShipCountry,
    string? ShipPhone, string? ShipFax, string? FreightCharge);

/// <summary>Header fields after the defaults. ExchangeRate is the document currency's value in base currency (BC-BR-412).</summary>
public sealed record SalesOrderHeader(
    long CustomerId, DateOnly SoDt, DateOnly ExpShipDt, DateOnly DueDt, string CurrencyCode, decimal ExchangeRate, string BillingFlow,
    string? Rep, string? Terms, string? ShipVia, SalesOrderShipTo ShipTo, string? Remarks);

/// <summary>A line as entered, with the defaults applied. DcCd is a sd_discount_rate code or null.</summary>
public sealed record SalesOrderLineInput(
    long? Id, long ItemId, string? ItemName, decimal SoQty, decimal SoPrc, string? DcCd, long TaxCodeId);

/// <summary>
/// A line with the values the server computed: DcRate from the discount code (SD-BR-404), SoAmt (SD-BR-005) which is the
/// supply amount before tax unless the tax code includes tax (SD-BR-032, BC-BR-544), and the estimated tax (BC-BR-547).
/// </summary>
public sealed record SalesOrderLine(
    long? Id, int Seq, long ItemId, string? ItemName, decimal SoQty, decimal SoPrc, string? DcCd, decimal DcRate, decimal SoAmt,
    long TaxCodeId, decimal SupplyAmt, decimal TaxAmt);

/// <summary>Header totals (SD-BR-007, SD-BR-034): supply, tax, total = supply + tax, and the total in base currency.</summary>
public sealed record SalesOrderTotals(decimal SupplyAmt, decimal TaxAmt, decimal TotSoAmt, decimal TotBaseAmt);

/// <summary>What the checks need from masters: item types of the items that exist, discount rates by code and tax code rates.</summary>
public sealed record SalesOrderMasters(
    IReadOnlyDictionary<long, string> ItemTypes, IReadOnlyDictionary<string, decimal> DiscountRates, IReadOnlyDictionary<long, TaxCodeRate> TaxRates);

/// <summary>Company settings for amounts: base currency, tax unit (BC-BR-543) and the SD module rounding mode (BC-BR-424).</summary>
public sealed record SalesOrderSettings(string BaseCurrencyCode, string TaxCalcMode, RoundingMode RoundingMode = RoundingMode.HalfUp);

/// <summary>An item with its type, standard sales price and price in the customer's price level, null when missing (SD-BR-010).</summary>
public sealed record PriceLevelReference(long ItemId, string ItemCd, string ItemType, decimal? StdPrc, decimal? LevelPrc);

/// <summary>A price the save would add to or change in the customer's price level (SD-BR-010).</summary>
public sealed record PriceLevelChange(long ItemId, string ItemCd, decimal SalesPrc);

/// <summary>
/// Sales order input checked on the server before save (SD-UC-002, docs/business-rules/SD/sales-order.md, pricing.md). Tax comes
/// from TaxCalculator; status rules are <see cref="SalesOrderStatus"/>.
/// </summary>
public static class SalesOrderEntryRules
{
    /// <summary>Line quantity is not above 0 (SD-BR-001). Arg: line number.</summary>
    public const string QtyCode = "SD-E-001";

    /// <summary>Line unit price is 0 (SD-BR-002). Arg: line number.</summary>
    public const string PriceCode = "SD-E-002";

    /// <summary>Line item is not in the item master (SD-BR-004). Arg: line number.</summary>
    public const string ItemCode = "SD-E-004";

    /// <summary>The customer of a saved order cannot change (SD-BR-023). No args.</summary>
    public const string CustomerLockedCode = "SD-E-023";

    /// <summary>Rate not above 0, not 1 for the base currency, or a currency change after lines exist (SD-BR-031). Args: currency, rate.</summary>
    public const string CurrencyCode = "SD-E-031";

    /// <summary>The billing flow is not one of the three values, or it changed after a shipment or invoice (SD-BR-035). Arg: value.</summary>
    public const string BillingFlowCode = "SD-E-035";

    /// <summary>The line's discount code is not in sd_discount_rate (SD-BR-404). Args: line number, code.</summary>
    public const string DiscountCode = "SD-E-404";

    /// <summary>A required field is empty. Arg: field name.</summary>
    public const string RequiredCode = "Common-E-005";

    /// <summary>The customer does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    /// <summary>Some prices differ from the standard and the customer's price level (SD-BR-010). Arg: item codes.</summary>
    public const string PriceLevelWarningCode = "SD-W-010";

    /// <summary>Request field that answers <see cref="PriceLevelWarningCode"/>.</summary>
    public const string UpdatePriceLevelField = "updatePriceLevel";

    /// <summary>The ship-to or freight differs from the customer's (SD-BR-009). No args.</summary>
    public const string ShipToWarningCode = "SD-W-009";

    /// <summary>Request field that answers <see cref="ShipToWarningCode"/>.</summary>
    public const string UpdateCustomerShipToField = "updateCustomerShipTo";

    /// <summary>Balance + this order exceeds the credit limit (SD-BR-309). Args: credit limit, balance after the save.</summary>
    public const string CreditLimitWarningCode = "SD-W-309";

    /// <summary>Request field that answers <see cref="CreditLimitWarningCode"/>.</summary>
    public const string ConfirmCreditLimitField = "confirmCreditLimit";

    /// <summary>Item types that never get a price level row (SD-BR-010, SD-BR-408).</summary>
    public static IReadOnlyList<string> ItemTypesWithoutPriceLevel { get; } = ["SERVICE", "SET"];

    /// <summary>
    /// Checks the header and lines and computes every amount. Lines are numbered 1..n in the order given.
    /// </summary>
    /// <param name="previousCurrencyCode">Currency of the stored order when it already has lines (SD-BR-031), else null.</param>
    /// <exception cref="DomainException">SD-E-001, 002, 004, 031, 035, 404; Common-E-005; BC-E-412 for an unknown currency; BC-E-423 for a rate with more than 10 decimals.</exception>
    public static (IReadOnlyList<SalesOrderLine> Lines, SalesOrderTotals Totals) Calculate(
        SalesOrderHeader header, IReadOnlyList<SalesOrderLineInput> lines, string? previousCurrencyCode, SalesOrderMasters masters,
        SalesOrderSettings settings, IAmountRounder rounder)
    {
        CheckHeader(header, previousCurrencyCode, settings, rounder);
        var decimals = rounder.DecimalsOf(header.CurrencyCode);
        var computed = new List<SalesOrderLine>(lines.Count);
        foreach (var (line, seq) in lines.Select((l, i) => (l, i + 1)))
        {
            var dcRate = CheckLine(line, seq, masters);
            computed.Add(new SalesOrderLine(
                line.Id, seq, line.ItemId, line.ItemName, line.SoQty, line.SoPrc, line.DcCd, dcRate,
                LineAmount(line.SoQty, line.SoPrc, dcRate, decimals, settings.RoundingMode), line.TaxCodeId, 0m, 0m));
        }

        // SD-BR-032: tax of the line amounts, as an estimate (BC-BR-547)
        var tax = new TaxCalculator(rounder).Calculate(
            [.. computed.Select(l => new TaxLineInput(l.TaxCodeId, l.SoAmt))], masters.TaxRates, settings.TaxCalcMode, header.CurrencyCode,
            settings.RoundingMode);
        computed = [.. computed.Select((l, i) => l with { SupplyAmt = tax.Lines[i].SupplyAmt, TaxAmt = tax.Lines[i].TaxAmt })];
        var totals = new SalesOrderTotals(
            tax.SupplyAmt, tax.TaxAmt, tax.TotalAmt,
            ExchangeRate.BaseAmount(tax.TotalAmt, header.ExchangeRate, settings.BaseCurrencyCode, rounder, settings.RoundingMode));
        return (computed, totals);
    }

    /// <summary>
    /// SD-BR-005: so_amt = round(qty × price × (1 + dc_rate / 100)) to the document currency decimals. The discount rate is a
    /// negative percentage (-33 = 33% off).
    /// </summary>
    public static decimal LineAmount(decimal qty, decimal price, decimal dcRate, int amountDecimals, RoundingMode mode = RoundingMode.HalfUp) =>
        Rounding.Round(qty * price * (1m + dcRate / 100m), amountDecimals, mode);

    /// <summary>SD-BR-023: a saved order keeps its customer.</summary>
    /// <exception cref="DomainException">SD-E-023 when the request names another customer.</exception>
    public static void EnsureSameCustomer(long storedCustomerId, long requestCustomerId)
    {
        if (storedCustomerId != requestCustomerId)
        {
            throw new DomainException(CustomerLockedCode);
        }
    }

    /// <summary>SD-BR-035: the billing flow of an order with a shipment or invoice that is not void stays as it is.</summary>
    /// <returns>True when the billing flow changes although such a document exists: the API answers 409 SD-E-035.</returns>
    public static bool BillingFlowLocked(string storedBillingFlow, string billingFlow, bool hasShipmentOrInvoice) =>
        hasShipmentOrInvoice && !string.Equals(storedBillingFlow, billingFlow, StringComparison.Ordinal);

    /// <summary>SD-BR-009: any ship-to field or the freight differs from the customer's. Terms are not compared.</summary>
    public static bool ShipToDiffers(SalesOrderShipTo order, SalesOrderShipTo customer) => Normalize(order) != Normalize(customer);

    /// <summary>
    /// SD-BR-010: items of new or changed lines whose price differs from both the standard sales price and the customer's price
    /// level price, in line order. Service and Set items, customers without a price level and orders outside the base currency
    /// (price levels have no currency) are left out. When an item is on several lines, its last line gives the price.
    /// </summary>
    /// <param name="stored">Saved lines by id; a line equal to its saved line is not compared.</param>
    public static IReadOnlyList<PriceLevelChange> PriceLevelChanges(
        IReadOnlyList<SalesOrderLine> lines, IReadOnlyDictionary<long, SalesOrderLine> stored, string? priceLevel, string currencyCode,
        string baseCurrencyCode, IReadOnlyDictionary<long, PriceLevelReference> references)
    {
        if (string.IsNullOrWhiteSpace(priceLevel) || currencyCode != baseCurrencyCode)
        {
            return [];
        }
        return [.. lines.Where(l => l.Id is not { } id || !stored.TryGetValue(id, out var saved) || Changed(l, saved))
            .GroupBy(l => l.ItemId)
            .Select(g => (Reference: references.GetValueOrDefault(g.Key), Price: g.Last().SoPrc))
            .Where(x => x.Reference is { } r && !ItemTypesWithoutPriceLevel.Contains(r.ItemType) && x.Price != r.StdPrc && x.Price != r.LevelPrc)
            .Select(x => new PriceLevelChange(x.Reference!.ItemId, x.Reference.ItemCd, x.Price))];
    }

    /// <summary>
    /// SD-BR-309: the customer's balance plus what this save adds goes over the credit limit. A limit of 0 or none is not checked,
    /// and a save that does not raise the order total is not either.
    /// </summary>
    /// <param name="previousTotal">Base currency total of the stored order; 0 for a new one.</param>
    public static bool CreditLimitExceeded(decimal? creditLimit, decimal balance, decimal newTotal, decimal previousTotal)
    {
        var increase = newTotal - previousTotal;
        return creditLimit is > 0 && increase > 0 && balance + increase > creditLimit;
    }

    private static bool Changed(SalesOrderLine line, SalesOrderLine saved) =>
        line.ItemId != saved.ItemId || line.SoQty != saved.SoQty || line.SoPrc != saved.SoPrc || line.DcCd != saved.DcCd
        || line.TaxCodeId != saved.TaxCodeId || line.ItemName != saved.ItemName;

    private static SalesOrderShipTo Normalize(SalesOrderShipTo s) => new(
        Clean(s.ShipName), Clean(s.ShipAddr1), Clean(s.ShipAddr2), Clean(s.ShipCity), Clean(s.ShipState), Clean(s.ShipZipCode),
        Clean(s.ShipCountry), Clean(s.ShipPhone), Clean(s.ShipFax), Clean(s.FreightCharge));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void CheckHeader(SalesOrderHeader header, string? previousCurrencyCode, SalesOrderSettings settings, IAmountRounder rounder)
    {
        if (header.CustomerId <= 0)
        {
            throw new DomainException(RequiredCode, "customerId");
        }
        if (string.IsNullOrWhiteSpace(header.Rep))
        {
            throw new DomainException(RequiredCode, "rep");
        }
        if (string.IsNullOrWhiteSpace(header.ShipTo.ShipZipCode))
        {
            throw new DomainException(RequiredCode, "shipZipCode");
        }
        if (!Customer.BillingFlows.Contains(header.BillingFlow))
        {
            throw new DomainException(BillingFlowCode, header.BillingFlow);
        }
        rounder.DecimalsOf(header.CurrencyCode);
        // SD-BR-031: rate above zero, 1 for the base currency, and no currency change once lines exist
        if (header.ExchangeRate <= 0
            || (header.CurrencyCode == settings.BaseCurrencyCode && header.ExchangeRate != 1m)
            || (previousCurrencyCode is not null && previousCurrencyCode != header.CurrencyCode))
        {
            throw new DomainException(CurrencyCode, header.CurrencyCode, header.ExchangeRate.ToString(CultureInfo.InvariantCulture));
        }
        ExchangeRate.EnsureDecimals(header.ExchangeRate, "exchangeRate");
    }

    // Returns the discount rate of the line's code (SD-BR-404)
    private static decimal CheckLine(SalesOrderLineInput line, int seq, SalesOrderMasters masters)
    {
        if (!masters.ItemTypes.ContainsKey(line.ItemId))
        {
            throw new DomainException(ItemCode, seq);
        }
        if (line.SoQty <= 0)
        {
            throw new DomainException(QtyCode, seq);
        }
        if (line.SoPrc == 0)
        {
            throw new DomainException(PriceCode, seq);
        }
        decimal dcRate = 0;
        if (line.DcCd is { } code && !masters.DiscountRates.TryGetValue(code, out dcRate))
        {
            throw new DomainException(DiscountCode, seq, code);
        }
        if (!masters.TaxRates.ContainsKey(line.TaxCodeId))
        {
            throw new DomainException(RequiredCode, "taxCodeId");
        }
        return dcRate;
    }
}
