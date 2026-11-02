using System.Data;
using QuroErp.Domain.Bc.Tax;

namespace QuroErp.Application.Bc.Tax;

/// <summary>bc_tax_area row.</summary>
public sealed record TaxAreaRecord(long Id, string TaxAreaCode, string TaxAreaName, string StateCode, DateTimeOffset UpdatedAt);

/// <summary>A list row with the latest rate line starting on or before the given day, if any.</summary>
public sealed record TaxAreaSummaryRecord(
    long Id, string TaxAreaCode, string TaxAreaName, string StateCode, DateOnly? EffectiveFrom, decimal? Rate);

/// <summary>US tax areas and their rate lines (bc_tax_area, bc_tax_area_rate). Callers pass their own transaction.</summary>
public interface ITaxAreaRepository
{
    /// <summary>Serializes tax area saves to the end of the transaction, so two saves cannot take the same code.</summary>
    Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Areas in code order, at most limit rows, each with the rate valid on today.</summary>
    Task<IReadOnlyList<TaxAreaSummaryRecord>> SearchAsync(
        string? keyword, string? stateCode, DateOnly today, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<TaxAreaRecord?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Rate lines of an area in start date order.</summary>
    Task<IReadOnlyList<TaxAreaRate>> FindRatesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The id of the area with this code, if any.</summary>
    Task<long?> FindIdByCodeAsync(string taxAreaCode, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>True when a tax code points at the area (bc_tax_code.tax_area_id).</summary>
    Task<bool> IsUsedByTaxCodesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<long> InsertAsync(TaxAreaFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves the header and moves updated_at; false when updated_at is no longer expectedUpdatedAt.</summary>
    Task<bool> UpdateAsync(
        long id, TaxAreaFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Makes the area's lines equal to fields.Rates: other start dates are deleted, the rest are added or changed.</summary>
    Task SaveRatesAsync(long id, TaxAreaFields fields, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the area and its lines; false when updated_at is no longer expectedUpdatedAt.</summary>
    Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}
