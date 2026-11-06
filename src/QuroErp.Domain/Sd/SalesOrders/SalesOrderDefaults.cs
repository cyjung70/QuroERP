using QuroErp.Domain.Bc.Tax;

namespace QuroErp.Domain.Sd.SalesOrders;

/// <summary>A line's tax code default (SD-BR-033) and the exemption certificate warning, if any (BC-BR-525).</summary>
public sealed record LineTaxDefault(long? TaxCodeId, string? Warning);

/// <summary>
/// Default values of a sales order and its lines (SD-BR-008, 016, 033, 401, 405, BC-BR-542, BC-BR-525). The user may change
/// all of them; the server fills only what the request leaves empty.
/// </summary>
public static class SalesOrderDefaults
{
    /// <summary>SD-BR-016: a new line orders 1.</summary>
    public const decimal Qty = 1m;

    // SD-BR-008: NET terms add days; every other term (COD_*, DUE_ON_RECEIPT, ...) is due on the document date
    private static readonly Dictionary<string, int> NetDays = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["NET_15"] = 15,
        ["NET_30"] = 30,
        ["NET_45"] = 45,
        ["NET_60"] = 60,
    };

    /// <summary>SD-BR-008: the payment due date of the terms.</summary>
    public static DateOnly DueDate(string? terms, DateOnly documentDt) =>
        terms is not null && NetDays.TryGetValue(terms, out var days) ? documentDt.AddDays(days) : documentDt;

    /// <summary>SD-BR-401: the customer's price level price, else the item's standard sales price, else 0 for every item type.</summary>
    public static decimal UnitPrice(decimal? levelPrice, decimal? standardPrice) => levelPrice ?? standardPrice ?? 0m;

    /// <summary>SD-BR-405: the customer's discount status when that code is in sd_discount_rate, else none.</summary>
    public static string? DiscountCode(string? customerDcStatus, IReadOnlyDictionary<string, decimal> discountRates) =>
        !string.IsNullOrWhiteSpace(customerDcStatus) && discountRates.ContainsKey(customerDcStatus.Trim()) ? customerDcStatus.Trim() : null;

    /// <summary>
    /// SD-BR-033, BC-BR-542, BC-BR-525: an item that is not taxable (US taxable = false, KR vat_kind = EXEMPT) gets the country's
    /// exempt code. Otherwise the customer's default tax code; for a US order whose ship-to state has a valid exemption
    /// certificate, the exempt code. Only expired certificates keep the customer's code and warn BC-W-525.
    /// </summary>
    /// <param name="exemptTaxCodeId">The country's exempt code in use (SALES_TAX_EXEMPT or VAT_EXEMPT), or null when there is none.</param>
    /// <param name="exemption">The certificate default of the ship-to state; null outside the US or without a state.</param>
    public static LineTaxDefault TaxCode(long? customerTaxCodeId, long? exemptTaxCodeId, bool itemTaxable, TaxExemptionDefault? exemption)
    {
        if (!itemTaxable && exemptTaxCodeId is not null)
        {
            return new LineTaxDefault(exemptTaxCodeId, null);
        }
        return exemption switch
        {
            { Exempt: true } when exemptTaxCodeId is not null => new LineTaxDefault(exemptTaxCodeId, null),
            { Warning: { } warning } => new LineTaxDefault(customerTaxCodeId, warning),
            _ => new LineTaxDefault(customerTaxCodeId, null),
        };
    }
}
