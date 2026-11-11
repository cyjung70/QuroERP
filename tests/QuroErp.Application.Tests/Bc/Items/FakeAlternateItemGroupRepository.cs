using System.Data;
using QuroErp.Application.Bc.Items;
using QuroErp.Domain.Bc.Items;

namespace QuroErp.Application.Tests.Bc.Items;

/// <summary>Groups in memory; Writes counts every delete, update and insert row.</summary>
public sealed class FakeAlternateItemGroupRepository : IAlternateItemGroupRepository
{
    private long _nextId = 100;

    public Dictionary<long, AlternateItemGroupRow> Rows { get; } = [];

    public int Writes { get; private set; }

    public int Locks { get; private set; }

    public (string? ItemClass, string? Keyword, int Limit) LastSearch { get; private set; }

    public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Locks++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlternateItemGroupRow>> SearchAsync(
        string? itemClass, string? keyword, int limit, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        LastSearch = (itemClass, keyword, limit);
        IReadOnlyList<AlternateItemGroupRow> rows = Rows.Values
            .Where(r => itemClass is null || r.ItemClass == itemClass)
            .Where(r => keyword is null || r.AltItemGroupCd.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || r.AltItemGroupName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.AltItemGroupCd, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        return Task.FromResult(rows);
    }

    public Task<IReadOnlyList<AlternateItemGroupRow>> FindForUpdateAsync(
        IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AlternateItemGroupRow>>(ids.Where(Rows.ContainsKey).Select(id => Rows[id]).ToList());

    public Task<IReadOnlyList<(long Id, string Code)>> FindCodeOwnersAsync(
        IReadOnlyCollection<string> codes, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(long Id, string Code)>>(
            Rows.Values.Where(r => codes.Contains(r.AltItemGroupCd)).Select(r => (r.Id, r.AltItemGroupCd)).ToList());

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            Writes++;
            Rows.Remove(id);
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        IReadOnlyList<(long Id, AlternateItemGroupFields Fields)> rows, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var (id, fields) in rows)
        {
            Writes++;
            Rows[id] = Rows[id] with
            {
                AltItemGroupCd = fields.AltItemGroupCd,
                AltItemGroupName = fields.AltItemGroupName,
                UpdatedAt = Rows[id].UpdatedAt.AddSeconds(1),
            };
        }
        return Task.CompletedTask;
    }

    public Task InsertAsync(
        IReadOnlyList<AlternateItemGroupFields> rows, string itemClass, string createdBy, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var fields in rows)
        {
            Writes++;
            var id = _nextId++;
            Rows[id] = new AlternateItemGroupRow(id, fields.AltItemGroupCd, fields.AltItemGroupName, itemClass, 0, DateTimeOffset.UnixEpoch);
        }
        return Task.CompletedTask;
    }
}
