using System.Data;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Bc.Tax;

/// <summary>
/// Rate of a tax code on a document date (BC-BR-514). Tax is always calculated with rates from here, so an external
/// tax engine can be added later as another implementation.
/// </summary>
public interface ITaxRateProvider
{
    /// <exception cref="DomainException">BC-E-513 when a SALES_TAX code has no tax area rate on the date.</exception>
    Task<TaxCodeRate> GetRateAsync(TaxCodeRow taxCode, DateOnly documentDate, IDbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>Default provider: the code's own rate, or the tax area table for SALES_TAX (BC-BR-503, BC-BR-513).</summary>
public sealed class TaxAreaRateProvider(ITaxRepository taxes) : ITaxRateProvider
{
    public async Task<TaxCodeRate> GetRateAsync(
        TaxCodeRow taxCode, DateOnly documentDate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var rate = taxCode.TaxKind switch
        {
            TaxKind.VatStandard => taxCode.Rate,
            // A SALES_TAX code without a tax area has no rate either
            TaxKind.SalesTax => TaxAreaRate.EffectiveOn(
                taxCode.TaxAreaId is { } areaId
                    ? (await taxes.FindTaxAreaRatesAsync(areaId, transaction, cancellationToken)).Select(r => new TaxAreaRate(r.EffectiveFrom, r.Rate))
                    : [],
                documentDate, taxCode.TaxCode),
            _ => 0m,
        };
        return new TaxCodeRate(taxCode.Id, rate, taxCode.PriceIncluded);
    }
}
