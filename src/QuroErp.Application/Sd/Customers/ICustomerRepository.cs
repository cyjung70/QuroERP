using System.Data;
using QuroErp.Domain.Bc.Tax;
using QuroErp.Domain.Sd.Customers;

namespace QuroErp.Application.Sd.Customers;

/// <summary>
/// A bc_customer row with the sum of its invoice and Credit Memo balances (SD-BR-308) and the code of its tax code. Values the
/// legacy data left empty are null.
/// </summary>
public sealed record CustomerRow(
    long Id,
    string CName,
    CustomerFields Fields,
    decimal BalAmt,
    string? TaxCode,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>List conditions as given by the caller; the repository escapes LIKE wildcards.</summary>
public sealed record CustomerSearch(string? CName, string? ValidFlag, int Offset, int Limit);

/// <summary>A stored exemption certificate of a customer (sd_customer_tax_exemption).</summary>
public sealed record CustomerTaxExemptionRow(long Id, TaxExemptionCertificate Certificate, DateTimeOffset UpdatedAt);

public interface ICustomerRepository
{
    /// <param name="forUpdate">Lock the customer row until the transaction ends.</param>
    Task<CustomerRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Matching customers in name order, and the number of all matches.</summary>
    Task<(IReadOnlyList<CustomerRow> Items, int Total)> SearchAsync(CustomerSearch search, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>A customer with exactly this (normalized) name exists (SD-BR-302).</summary>
    Task<bool> NameExistsAsync(string cName, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Names of other customers with this business number, in name order (BC-BR-522).</summary>
    /// <param name="exceptId">The customer being saved; null for a new one.</param>
    Task<IReadOnlyList<string>> FindNamesByBusinessRegNoAsync(
        string businessRegNo, long? exceptId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>The new id.</returns>
    Task<long> InsertAsync(Customer customer, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Saves the editable values only when updated_at still equals expectedUpdatedAt. The name is not changed.</summary>
    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> UpdateAsync(
        Customer customer, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The customer has a sales order, shipment, invoice or Credit Memo, receipt or opening balance (SD-BR-307).</summary>
    Task<bool> HasDocumentsAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Deletes the customer and its exemption certificates.</summary>
    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The customer's exemption certificates in state and certificate number order.</summary>
    Task<IReadOnlyList<CustomerTaxExemptionRow>> FindExemptionsAsync(long customerId, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertExemptionAsync(
        long customerId, TaxExemptionCertificate certificate, string userId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> UpdateExemptionAsync(
        long id, TaxExemptionCertificate certificate, DateTimeOffset expectedUpdatedAt, string userId, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <returns>False when updated_at changed or the row is gone.</returns>
    Task<bool> DeleteExemptionAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}
