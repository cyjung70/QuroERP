using QuroErp.Domain.Common;

namespace QuroErp.Domain.Bc.Items;

/// <summary>A stored item as the alternate item check sees it (MM-BR-812). GroupId and GroupCd are its current group, if any.</summary>
public sealed record AlternateItemCandidate(long Id, string ItemCd, string? ItemType, string? ValidFlag, long? GroupId, string? GroupCd);

/// <summary>A changed grid row: the item that was on the row and the item code now on it (MM-BR-811).</summary>
public sealed record AlternateItemRowChange(long ItemId, string? ItemCd);

/// <summary>The item writes of one save (MM-BR-811).</summary>
/// <param name="JoinIds">Items that get this group.</param>
/// <param name="LeaveIds">Items whose group is cleared; the items themselves stay.</param>
/// <param name="Moved">Joining items that are in another group now; they move only after the user confirms (MM-BR-812).</param>
public sealed record AlternateItemPlan(IReadOnlyList<long> JoinIds, IReadOnlyList<long> LeaveIds, IReadOnlyList<AlternateItemCandidate> Moved);

/// <summary>Alternate item group members (BC-UC-012, MM-BR-811~814).</summary>
public static class AlternateItemRules
{
    /// <summary>
    /// MM-BR-812: an item that is unknown, not an Inventory item in use, or on two rows (422, arg: item code). Also 409 with a second arg,
    /// the other group code, when the item is in another group and the move is not confirmed.
    /// </summary>
    public const string InvalidItemCode = "MM-E-812";

    /// <summary>Trimmed, upper-cased item codes of the rows that bring an item in: added rows, then changed rows (MM-BR-813 row order).</summary>
    /// <exception cref="DomainException">MM-E-802 for a row without an item code.</exception>
    public static IReadOnlyList<string> JoinCodes(IEnumerable<string?> added, IEnumerable<AlternateItemRowChange> updated) =>
        added.Concat(updated.Select(u => u.ItemCd)).Select(Normalize).ToList();

    /// <summary>
    /// The writes of one save. Every row is checked in row order and the first bad row stops the save, so nothing is written (MM-BR-813).
    /// An item that leaves and comes back on another row stays in the group.
    /// </summary>
    /// <param name="members">Ids of the items in the group now.</param>
    /// <param name="leaving">Ids of deleted rows and the old items of changed rows; all of them are members.</param>
    /// <param name="joinCodes">From <see cref="JoinCodes"/>.</param>
    /// <param name="candidates">The stored items of <paramref name="joinCodes"/> by item code; a code without one is unknown.</param>
    /// <exception cref="DomainException">MM-E-812 with the item code of the first bad row.</exception>
    public static AlternateItemPlan Plan(
        long groupId,
        IReadOnlyCollection<long> members,
        IReadOnlyCollection<long> leaving,
        IReadOnlyList<string> joinCodes,
        IReadOnlyDictionary<string, AlternateItemCandidate> candidates)
    {
        var staying = members.Except(leaving).ToHashSet();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var joining = new List<long>();
        var moved = new List<AlternateItemCandidate>();
        foreach (var code in joinCodes)
        {
            if (!candidates.TryGetValue(code, out var item)
                || item.ItemType != Item.DefaultItemType
                || item.ValidFlag == Item.ValidFlagNo
                || !seen.Add(code)
                || staying.Contains(item.Id))
            {
                throw new DomainException(InvalidItemCode, code);
            }
            joining.Add(item.Id);
            if (item.GroupId is { } other && other != groupId)
            {
                moved.Add(item);
            }
        }
        var joined = joining.ToHashSet();
        return new AlternateItemPlan(
            joining.Where(id => !members.Contains(id)).ToList(),
            leaving.Distinct().Where(id => !joined.Contains(id)).ToList(),
            moved);
    }

    private static string Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? throw new DomainException(Item.RequiredCode, nameof(Item.ItemCd)) : code.Trim().ToUpperInvariant();
}
