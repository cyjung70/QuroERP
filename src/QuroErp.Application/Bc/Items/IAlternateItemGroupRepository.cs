using System.Data;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Application.Bc.Items;

public sealed record AlternateItemGroupRow(
    long Id, string AltItemGroupCd, string AltItemGroupName, string ItemClass, long ItemCount, DateTimeOffset UpdatedAt);

/// <summary>Alternate item groups (BC-UC-019).</summary>
public interface IAlternateItemGroupRepository
{
    /// <summary>Serializes group saves, so the code check sees every committed group.</summary>
    Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Groups in code order; a null condition matches every group.</summary>
    Task<IReadOnlyList<AlternateItemGroupRow>> SearchAsync(
        string? itemClass, string? keyword, int limit, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The stored groups of these ids, locked for the save.</summary>
    Task<IReadOnlyList<AlternateItemGroupRow>> FindForUpdateAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Ids and codes of the stored groups that have one of these codes.</summary>
    Task<IReadOnlyList<(long Id, string Code)>> FindCodeOwnersAsync(
        IReadOnlyCollection<string> codes, IDbTransaction transaction, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Changes code and name; codes may be swapped between the given groups.</summary>
    Task UpdateAsync(
        IReadOnlyList<(long Id, AlternateItemGroupFields Fields)> rows, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken);

    Task InsertAsync(
        IReadOnlyList<AlternateItemGroupFields> rows, string itemClass, string createdBy, IDbTransaction transaction,
        CancellationToken cancellationToken);
}
