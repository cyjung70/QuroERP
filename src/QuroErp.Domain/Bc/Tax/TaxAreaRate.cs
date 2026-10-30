using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Tax;

/// <summary>Combined rate of a US tax area from a start date (bc_tax_area_rate, BC-BR-512). Rate is a percentage.</summary>
public sealed record TaxAreaRate(DateOnly EffectiveFrom, decimal Rate)
{
    /// <summary>No tax area rate on the document date. Args: tax code, document date.</summary>
    public const string NoRateCode = "BC-E-513";

    /// <summary>The rate on the document date: the latest row starting on or before it (BC-BR-513).</summary>
    /// <exception cref="DomainException">BC-E-513 when no row starts on or before the date.</exception>
    public static decimal EffectiveOn(IEnumerable<TaxAreaRate> rates, DateOnly documentDate, string taxCode) =>
        rates.Where(r => r.EffectiveFrom <= documentDate).MaxBy(r => r.EffectiveFrom)?.Rate
            ?? throw new DomainException(NoRateCode, taxCode, documentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
}
