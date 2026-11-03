using System.Data;
using QuroErp.Application.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Api.Tests.Bc.Items;

/// <summary>
/// Alternate item groups in memory: 601 BOLT (PART, 2 items), 602 NUT (PART), 603 GEAR (ZZBC1). Ids avoid 1 and 2, which the
/// translation fake answers for every target.
/// </summary>
public sealed class InMemoryAlternateItemGroupRepository : IAlternateItemGroupRepository
{
    public const long BoltId = 601;
    public const long NutId = 602;

    public static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _lock = new();
    private long _nextId = 610;

    private readonly Dictionary<long, AlternateItemGroupRow> _rows = new()
    {
        [BoltId] = new(BoltId, "BOLT", "Bolts", "PART", 2, Created),
        [NutId] = new(NutId, "NUT", "Nuts", "PART", 0, Created),
        [603] = new(603, "GEAR", "Gears", "ZZBC1", 0, Created),
    };

    public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<AlternateItemGroupRow>> SearchAsync(
        string? itemClass, string? keyword, int limit, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<AlternateItemGroupRow> rows = _rows.Values
                .Where(r => itemClass is null || r.ItemClass == itemClass)
                .Where(r => keyword is null || r.AltItemGroupCd.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || r.AltItemGroupName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.AltItemGroupCd, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<AlternateItemGroupRow>> FindForUpdateAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<AlternateItemGroupRow>>(ids.Where(_rows.ContainsKey).Select(id => _rows[id]).ToList());
        }
    }

    public Task<IReadOnlyList<(long Id, string Code)>> FindCodeOwnersAsync(
        IReadOnlyCollection<string> codes, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<(long Id, string Code)>>(
                _rows.Values.Where(r => codes.Contains(r.AltItemGroupCd)).Select(r => (r.Id, r.AltItemGroupCd)).ToList());
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var id in ids)
            {
                _rows.Remove(id);
            }
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(
        IReadOnlyList<(long Id, AlternateItemGroupFields Fields)> rows, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var (id, fields) in rows)
            {
                _rows[id] = _rows[id] with
                {
                    AltItemGroupCd = fields.AltItemGroupCd,
                    AltItemGroupName = fields.AltItemGroupName,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
            }
            return Task.CompletedTask;
        }
    }

    public Task InsertAsync(
        IReadOnlyList<AlternateItemGroupFields> rows, string itemClass, string createdBy, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var fields in rows)
            {
                var id = _nextId++;
                _rows[id] = new AlternateItemGroupRow(id, fields.AltItemGroupCd, fields.AltItemGroupName, itemClass, 0, DateTimeOffset.UtcNow);
            }
            return Task.CompletedTask;
        }
    }
}
