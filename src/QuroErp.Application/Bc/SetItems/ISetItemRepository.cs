using System.Data;
using QuroErp.Domain.Bc.SetItems;

namespace QuroErp.Application.Bc.SetItems;

/// <summary>An sd_bom row with its component's code, name and unit.</summary>
public sealed record SetComponentRow(
    long Id, int Seq, long ItemId, string ItemCd, string? ItemName, string? Unit, decimal Qty, DateTimeOffset UpdatedAt);

/// <summary>Set item composition (sd_bom, AS IS P_BOM). Callers pass their own transaction.</summary>
public interface ISetItemRepository
{
    /// <summary>The item with this code; null when there is none.</summary>
    /// <param name="forUpdate">
    /// Lock the item row until the transaction ends. Saves of one set wait for each other, and stock postings of the set wait for
    /// the save (inventory.md step 0).
    /// </param>
    Task<SetItemCandidate?> FindItemAsync(string itemCd, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Items with these codes; codes that do not exist are left out.</summary>
    Task<IReadOnlyList<SetItemCandidate>> FindItemsAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The set's components in sequence order.</summary>
    Task<IReadOnlyList<SetComponentRow>> FindComponentsAsync(long setItemId, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertAsync(
        long setItemId, int seq, long componentItemId, decimal qty, string user, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Changes the component and qty; the sequence stays (SD-BR-505).</summary>
    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> UpdateAsync(
        long id, long componentItemId, decimal qty, DateTimeOffset updatedAt, string user, IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <returns>false when updated_at no longer equals <paramref name="updatedAt"/>.</returns>
    Task<bool> DeleteAsync(long id, DateTimeOffset updatedAt, IDbTransaction transaction, CancellationToken cancellationToken);
}
