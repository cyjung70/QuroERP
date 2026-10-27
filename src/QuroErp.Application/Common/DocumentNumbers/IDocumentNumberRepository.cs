using System.Data;

namespace QuroErp.Application.Common.DocumentNumbers;

/// <summary>sys_document_number: the last number issued per (transaction type, period).</summary>
public interface IDocumentNumberRepository
{
    /// <summary>Locks the counter row until the transaction ends, so two transactions never get the same number.</summary>
    /// <returns>The last number issued, or null when the counter does not exist yet (nothing is locked then).</returns>
    Task<string?> LockLastAsync(string transType, string period, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the counter with its first number. The new row stays locked until the transaction ends.
    /// When another transaction is creating the same counter, waits for it to end.
    /// </summary>
    /// <returns>false when another transaction created the counter first.</returns>
    Task<bool> TryCreateAsync(string transType, string period, string firstNo, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    Task SaveLastAsync(string transType, string period, string lastNo, string user, IDbTransaction transaction, CancellationToken cancellationToken);
}
