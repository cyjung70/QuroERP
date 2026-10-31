using System.Data;

namespace QuroErp.Application.Bc.Tax;

/// <summary>bc_tax_code row. Rate is a percentage; SALES_TAX codes take theirs from TaxAreaId (BC-BR-503).</summary>
public sealed record TaxCodeRow(long Id, string TaxCode, string TaxKind, decimal Rate, long? TaxAreaId, bool PriceIncluded, bool UseYn);

/// <summary>bc_tax_area_rate row (BC-BR-512).</summary>
public sealed record TaxAreaRateRow(DateOnly EffectiveFrom, decimal Rate);

/// <summary>sd_customer_tax_exemption row (BC-BR-524).</summary>
public sealed record TaxExemptionRow(string CertificateNo, string StateCode, DateOnly? ValidTo);

/// <summary>Tax codes, US tax area rates and exemption certificates (tax.md). Callers pass their own transaction.</summary>
public interface ITaxRepository
{
    /// <summary>Tax codes with these ids, in id order. Unknown ids are left out.</summary>
    Task<IReadOnlyList<TaxCodeRow>> FindTaxCodesAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>All rate rows of a tax area, in effective_from order.</summary>
    Task<IReadOnlyList<TaxAreaRateRow>> FindTaxAreaRatesAsync(long taxAreaId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Exemption certificates of a customer for one state, in certificate_no order.</summary>
    Task<IReadOnlyList<TaxExemptionRow>> FindExemptionsAsync(
        long customerId, string stateCode, IDbTransaction transaction, CancellationToken cancellationToken);
}
