using System.Data;
using QuroErp.Application.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Api.Tests.Bc.Items;

/// <summary>
/// Alternate item groups in memory: 501 BOLT with items A510 and A511, 502 NUT with B521. B520 is free, S522 is a service item.
/// Ids avoid 1 and 2, which the translation fake answers for every target.
/// </summary>
public sealed class InMemoryAlternateItemRepository : IAlternateItemRepository
{
    public const long BoltId = 501;
    public const long NutId = 502;

    public static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _lock = new();

    private readonly Dictionary<long, AlternateItemGroupRecord> _groups = new()
    {
        [BoltId] = new(BoltId, "BOLT", "Bolts", Created),
        [NutId] = new(NutId, "NUT", "Nuts", Created),
    };

    private readonly Dictionary<string, AlternateItemCandidate> _items = new AlternateItemCandidate[]
    {
        new(510, "A510", "INVENTORY", "Y", BoltId, "BOLT"),
        new(511, "A511", "INVENTORY", "Y", BoltId, "BOLT"),
        new(520, "B520", "INVENTORY", "Y", null, null),
        new(521, "B521", "INVENTORY", "Y", NutId, "NUT"),
        new(522, "S522", "SERVICE", "Y", null, null),
    }.ToDictionary(i => i.ItemCd);

    public Task<AlternateItemGroupRecord?> FindGroupByCodeAsync(string groupCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_groups.Values.FirstOrDefault(g => g.AltItemGroupCd == groupCode));
        }
    }

    public Task<AlternateItemGroupRecord?> FindGroupAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_groups.GetValueOrDefault(id));
        }
    }

    public Task<IReadOnlyList<AlternateItemMemberRecord>> FindMembersAsync(long groupId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<AlternateItemMemberRecord> rows = _items.Values
                .Where(i => i.GroupId == groupId)
                .OrderBy(i => i.ItemCd, StringComparer.Ordinal)
                .Select(i => new AlternateItemMemberRecord(i.Id, i.ItemCd, "Item " + i.ItemCd))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<AlternateItemCandidate>> FindCandidatesAsync(
        IReadOnlyCollection<string> itemCds, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<AlternateItemCandidate> rows = itemCds.Where(_items.ContainsKey).Select(c => _items[c]).ToList();
            return Task.FromResult(rows);
        }
    }

    public Task SaveAsync(
        long groupId, IReadOnlyCollection<long> joinIds, IReadOnlyCollection<long> leaveIds, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var group = _groups[groupId];
            foreach (var item in _items.Values.ToList())
            {
                if (joinIds.Contains(item.Id))
                {
                    if (item.GroupId is { } from)
                    {
                        _groups[from] = _groups[from] with { UpdatedAt = DateTimeOffset.UtcNow };
                    }
                    _items[item.ItemCd] = item with { GroupId = groupId, GroupCd = group.AltItemGroupCd };
                }
                else if (leaveIds.Contains(item.Id))
                {
                    _items[item.ItemCd] = item with { GroupId = null, GroupCd = null };
                }
            }
            _groups[groupId] = group with { UpdatedAt = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }
    }
}
