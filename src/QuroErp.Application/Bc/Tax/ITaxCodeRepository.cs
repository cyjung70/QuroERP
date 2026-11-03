using System.Data;
using QuroErp.Domain.Bc.Tax;

namespace QuroErp.Application.Bc.Tax;

/// <summary>A stored tax code without its names. TaxAreaCode is the code of TaxAreaId.</summary>
public sealed record TaxCodeRecord(
    long Id, string TaxCode, string TaxKind, decimal Rate, long? TaxAreaId, string? TaxAreaCode, bool PriceIncluded, bool UseYn,
    DateTimeOffset UpdatedAt);

/// <summary>Tax code master (bc_tax_code, bc_tax_code_translation). Callers pass their own transaction.</summary>
public interface ITaxCodeRepository
{
    /// <summary>Every tax code in tax_code order. forUpdate locks the rows so tax code saves run one at a time.</summary>
    Task<IReadOnlyList<TaxCodeRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The given tax code ids that a document line stores (TaxCodeRules.DocumentColumns).</summary>
    Task<IReadOnlySet<long>> FindUsedByDocumentsAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The given tax code ids that a customer or vendor has as default (TaxCodeRules.PartnerColumns).</summary>
    Task<IReadOnlySet<long>> FindUsedByPartnersAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The given tax area ids that exist (bc_tax_area).</summary>
    Task<IReadOnlySet<long>> FindTaxAreasAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(string taxCode, TaxCodeFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when the row changed after expectedUpdatedAt.</returns>
    Task<bool> UpdateAsync(
        long id, string taxCode, TaxCodeFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>Sets the given names and removes the null ones. Languages not given keep their names (BC-BR-405).</summary>
    Task SaveNamesAsync(
        long taxCodeId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the rows; their translations go with them.</summary>
    Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);
}
