using System.Data;

namespace QuroErp.Application.Common.DocumentNumbers;

/// <summary>sys_document_number: the last number issued per transaction type.</summary>
public interface IDocumentNumberRepository
{
    /// <summary>
    /// Locks the row for transType (creating it when missing) until the transaction ends, so two transactions
    /// never get the same number.
    /// </summary>
    /// <returns>The last number issued, or null when none was issued yet.</returns>
    Task<string?> LockLastAsync(string transType, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    Task SaveLastAsync(string transType, string lastNo, string user, IDbTransaction transaction, CancellationToken cancellationToken);
}
