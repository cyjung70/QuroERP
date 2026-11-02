using System.Data;
using QuroErp.Application.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Application.Tests.Bc.Items;

/// <summary>Groups and items in memory; Saves records each write.</summary>
public sealed class FakeAlternateItemRepository : IAlternateItemRepository
{
    public Dictionary<long, AlternateItemGroupRecord> Groups { get; } = [];

    /// <summary>Items by code; the group is the stored one.</summary>
    public Dictionary<string, AlternateItemCandidate> Items { get; } = [];

    public List<(long GroupId, IReadOnlyCollection<long> JoinIds, IReadOnlyCollection<long> LeaveIds, string UpdatedBy)> Saves { get; } = [];

    public List<bool> GroupLocks { get; } = [];

    public Task<AlternateItemGroupRecord?> FindGroupByCodeAsync(string groupCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Groups.Values.FirstOrDefault(g => g.AltItemGroupCd == groupCode));

    public Task<AlternateItemGroupRecord?> FindGroupAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        GroupLocks.Add(forUpdate);
        return Task.FromResult(Groups.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<AlternateItemMemberRecord>> FindMembersAsync(long groupId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        IReadOnlyList<AlternateItemMemberRecord> rows = Items.Values
            .Where(i => i.GroupId == groupId)
            .OrderBy(i => i.ItemCd, StringComparer.Ordinal)
            .Select(i => new AlternateItemMemberRecord(i.Id, i.ItemCd, "Name " + i.ItemCd))
            .ToList();
        return Task.FromResult(rows);
    }

    public Task<IReadOnlyList<AlternateItemCandidate>> FindCandidatesAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        IReadOnlyList<AlternateItemCandidate> rows = itemCds.Where(Items.ContainsKey).Select(c => Items[c]).ToList();
        return Task.FromResult(rows);
    }

    public Task SaveAsync(
        long groupId, IReadOnlyCollection<long> joinIds, IReadOnlyCollection<long> leaveIds, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        Saves.Add((groupId, joinIds, leaveIds, updatedBy));
        var group = Groups[groupId];
        foreach (var item in Items.Values.ToList())
        {
            if (joinIds.Contains(item.Id))
            {
                Items[item.ItemCd] = item with { GroupId = groupId, GroupCd = group.AltItemGroupCd };
            }
            else if (leaveIds.Contains(item.Id))
            {
                Items[item.ItemCd] = item with { GroupId = null, GroupCd = null };
            }
        }
        Groups[groupId] = group with { UpdatedAt = group.UpdatedAt.AddSeconds(1) };
        return Task.CompletedTask;
    }
}
