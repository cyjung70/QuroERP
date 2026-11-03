using System.Data;
using QuroErp.Domain.Mm.Vendors;

namespace QuroErp.Application.Mm.Vendors;

/// <summary>
/// A bc_vendor row with the sum of its goods receipt balances (MM-BR-858) and the code of its tax code. Values the legacy data
/// left empty are null.
/// </summary>
public sealed record VendorRow(
    long Id,
    string VName,
    VendorFields Fields,
    decimal BalAmt,
    string? TaxCode,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>List conditions as given by the caller; the repository escapes LIKE wildcards.</summary>
public sealed record VendorSearch(string? VName, string? ValidFlag, int Offset, int Limit);

public interface IVendorRepository
{
    /// <param name="forUpdate">Lock the vendor row until the transaction ends.</param>
    Task<VendorRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The vendor before or after the given one in name order; null at either end.</summary>
    Task<VendorRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Matching vendors in name order, and the number of all matches.</summary>
    Task<(IReadOnlyList<VendorRow> Items, int Total)> SearchAsync(VendorSearch search, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>A vendor with exactly this (normalized) name exists (MM-BR-852).</summary>
    Task<bool> NameExistsAsync(string vName, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Names of other vendors with this business number, in name order (BC-BR-522).</summary>
    /// <param name="exceptId">The vendor being saved; null for a new one.</param>
    Task<IReadOnlyList<string>> FindNamesByBusinessRegNoAsync(
        string businessRegNo, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>The new id.</returns>
    Task<long> InsertAsync(Vendor vendor, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves the editable values only when updated_at still equals expectedUpdatedAt. The name is not changed.</summary>
    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> UpdateAsync(
        Vendor vendor, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Used by a purchase order, goods receipt or payment, or the only vendor of a purchase price group that has prices (MM-BR-857).
    /// </summary>
    Task<bool> IsInUseAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}
