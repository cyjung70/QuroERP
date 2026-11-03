using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Domain.Bc.Tax;

/// <summary>Rate of one tax code on the document date. Rate is a percentage (10 = 10%).</summary>
/// <param name="PriceIncluded">The line amount already includes the tax (BC-BR-544).</param>
public sealed record TaxCodeRate(long TaxCodeId, decimal Rate, bool PriceIncluded);

/// <summary>A document line to tax: its tax code and line amount (qty × price, already rounded by the document rule).</summary>
public sealed record TaxLineInput(long TaxCodeId, decimal Amount);

public sealed record TaxLineResult(long TaxCodeId, decimal SupplyAmt, decimal TaxAmt);

/// <summary>Lines in the order given, and the header sums (BC-BR-541).</summary>
public sealed record TaxResult(IReadOnlyList<TaxLineResult> Lines, decimal SupplyAmt, decimal TaxAmt)
{
    /// <summary>Document total = supply + tax (tot_*_amt).</summary>
    public decimal TotalAmt => SupplyAmt + TaxAmt;
}

/// <summary>
/// The only place that computes tax (tax.md "계산"). Documents, screens and SQL never compute tax themselves.
/// </summary>
public interface ITaxCalculator
{
    /// <param name="rates">Rate of every tax code the lines use, from ITaxRateProvider.</param>
    /// <param name="taxCalcMode">TaxCalcMode.Document or TaxCalcMode.Line (bc_company.tax_calc_mode, BC-BR-543).</param>
    /// <param name="roundingMode">Rounding mode of the document's module (BC-BR-424, BC-BR-545).</param>
    /// <exception cref="Common.DomainException">BC-E-412 when the currency is not in the currency master.</exception>
    TaxResult Calculate(
        IReadOnlyList<TaxLineInput> lines, IReadOnlyDictionary<long, TaxCodeRate> rates, string taxCalcMode, string currencyCode,
        RoundingMode roundingMode);
}

public sealed class TaxCalculator(IAmountRounder rounder) : ITaxCalculator
{
    public TaxResult Calculate(
        IReadOnlyList<TaxLineInput> lines, IReadOnlyDictionary<long, TaxCodeRate> rates, string taxCalcMode, string currencyCode,
        RoundingMode roundingMode)
    {
        if (!TaxCalcMode.All.Contains(taxCalcMode))
        {
            throw new ArgumentException($"Unknown tax calculation mode '{taxCalcMode}'.", nameof(taxCalcMode));
        }
        var amounts = lines.Select(l => rounder.Round(l.Amount, currencyCode)).ToArray();
        var taxes = new decimal[lines.Count];
        foreach (var group in Enumerable.Range(0, lines.Count).GroupBy(i => lines[i].TaxCodeId))
        {
            var rate = rates.TryGetValue(group.Key, out var found)
                ? found
                : throw new ArgumentException($"No rate for tax code {group.Key}.", nameof(rates));
            var indexes = group.ToList();
            if (taxCalcMode == TaxCalcMode.Line)
            {
                indexes.ForEach(i => taxes[i] = TaxOf(amounts[i], rate, currencyCode, roundingMode));
            }
            else
            {
                Allocate(indexes, amounts, taxes, TaxOf(indexes.Sum(i => amounts[i]), rate, currencyCode, roundingMode), currencyCode);
            }
        }

        // A price-included amount holds the tax, so its supply is what is left (BC-BR-544)
        var results = lines.Select((l, i) => new TaxLineResult(
            l.TaxCodeId, rates[l.TaxCodeId].PriceIncluded ? amounts[i] - taxes[i] : amounts[i], taxes[i])).ToList();
        return new TaxResult(results, results.Sum(r => r.SupplyAmt), results.Sum(r => r.TaxAmt));
    }

    // Tax of an amount: amount × rate, or amount × rate / (100 + rate) when the amount includes the tax.
    // Rounding the tax (not the supply) keeps the module mode on the tax, e.g. DOWN cuts tax below one won (BC-BR-545).
    private decimal TaxOf(decimal amount, TaxCodeRate rate, string currencyCode, RoundingMode roundingMode) =>
        rounder.Round(amount * rate.Rate / (rate.PriceIncluded ? 100m + rate.Rate : 100m), currencyCode, roundingMode);

    // DOCUMENT mode: split the tax of the tax code over its lines by amount; the remainder goes to the
    // largest line, the first one on a tie (BC-BR-543)
    private void Allocate(List<int> indexes, decimal[] amounts, decimal[] taxes, decimal groupTax, string currencyCode)
    {
        var groupAmount = indexes.Sum(i => amounts[i]);
        if (groupAmount == 0m)
        {
            indexes.ForEach(i => taxes[i] = 0m);
            return;
        }
        indexes.ForEach(i => taxes[i] = rounder.Round(groupTax * amounts[i] / groupAmount, currencyCode));
        var largest = indexes.MaxBy(i => Math.Abs(amounts[i]));
        taxes[largest] += groupTax - indexes.Sum(i => taxes[i]);
    }
}
