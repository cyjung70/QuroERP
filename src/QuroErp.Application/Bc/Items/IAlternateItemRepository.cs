using System.Data;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Application.Bc.Items;

public sealed record AlternateItemGroupRecord(long Id, string AltItemGroupCd, string AltItemGroupName, DateTimeOffset UpdatedAt);

public sealed record AlternateItemMemberRecord(long Id, string ItemCd, string? ItemName);

/// <summary>Alternate item group members (BC-UC-012). Items point to the group by id (MM-BR-814).</summary>
public interface IAlternateItemRepository
{
    Task<AlternateItemGroupRecord?> FindGroupByCodeAsync(string groupCode, IDbTransaction transaction, CancellationToken cancellationToken);

    Task<AlternateItemGroupRecord?> FindGroupAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The items of the group in item code order.</summary>
    Task<IReadOnlyList<AlternateItemMemberRecord>> FindMembersAsync(long groupId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>The stored items of these codes with their current group, locked for the save.</summary>
    Task<IReadOnlyList<AlternateItemCandidate>> FindCandidatesAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the joining items this group and clears the group of the leaving items. The group's updated_at changes, and so does that of
    /// every group a joining item leaves.
    /// </summary>
    Task SaveAsync(
        long groupId, IReadOnlyCollection<long> joinIds, IReadOnlyCollection<long> leaveIds, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken);
}
