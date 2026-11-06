using QuroErp.Domain.Bc.Localization;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Domain.Sd.Shipments;

/// <summary>A shipment line the SHIP_AND_BILL invoice bills: its qty, price, discount and tax code (SD-BR-167).</summary>
public sealed record ShipmentInvoiceInput(int Seq, decimal Qty, decimal Prc, decimal DcRate, long TaxCodeId);

/// <summary>An invoice line's amounts: DnAmt (SD-BR-005), supply and tax at the invoice date's rate (SD-BR-132).</summary>
public sealed record ShipmentInvoiceLineAmounts(int Seq, decimal DnAmt, decimal SupplyAmt, decimal TaxAmt);

/// <summary>Invoice totals (SD-BR-109): total = balance = supply + tax, and the total in base currency.</summary>
public sealed record ShipmentInvoiceTotals(decimal SupplyAmt, decimal TaxAmt, decimal TotDnAmt, decimal TotBaseAmt);

/// <summary>Company settings for amounts: base currency, tax unit (BC-BR-543) and the SD module rounding mode (BC-BR-424).</summary>
public sealed record ShipmentSettings(string BaseCurrencyCode, string TaxCalcMode, RoundingMode RoundingMode = RoundingMode.HalfUp);

/// <summary>
/// The invoice a SHIP_AND_BILL shipment makes in the same save (SD-BR-167): one line per shipment line, amounts as SD-BR-005 and
/// SD-BR-109, tax recalculated at the invoice date's rate (SD-BR-132, BC-BR-547).
/// </summary>
public static class ShipmentInvoice
{
    /// <summary>Computes every line and the totals. The whole invoice is recalculated, so document tax rounding stays right.</summary>
    /// <param name="rates">Rates of the line tax codes at the invoice date.</param>
    public static (IReadOnlyList<ShipmentInvoiceLineAmounts> Lines, ShipmentInvoiceTotals Totals) Calculate(
        IReadOnlyList<ShipmentInvoiceInput> lines, string currencyCode, decimal exchangeRate, IReadOnlyDictionary<long, TaxCodeRate> rates,
        ShipmentSettings settings, IAmountRounder rounder)
    {
        var decimals = rounder.DecimalsOf(currencyCode);
        var amounts = lines.Select(l => SalesOrderEntryRules.LineAmount(l.Qty, l.Prc, l.DcRate, decimals, settings.RoundingMode)).ToList();
        var tax = new TaxCalculator(rounder).Calculate(
            [.. lines.Select((l, i) => new TaxLineInput(l.TaxCodeId, amounts[i]))], rates, settings.TaxCalcMode, currencyCode, settings.RoundingMode);
        var computed = lines.Select((l, i) => new ShipmentInvoiceLineAmounts(l.Seq, amounts[i], tax.Lines[i].SupplyAmt, tax.Lines[i].TaxAmt)).ToList();
        var totals = new ShipmentInvoiceTotals(
            tax.SupplyAmt, tax.TaxAmt, tax.TotalAmt,
            ExchangeRate.BaseAmount(tax.TotalAmt, exchangeRate, settings.BaseCurrencyCode, rounder, settings.RoundingMode));
        return (computed, totals);
    }
}
