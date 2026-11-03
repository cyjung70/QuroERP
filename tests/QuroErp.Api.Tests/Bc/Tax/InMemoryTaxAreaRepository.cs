using System.Data;
using QuroErp.Application.Bc.Tax;
using QuroErp.Domain.Bc.Tax;

namespace QuroErp.Api.Tests.Bc.Tax;

/// <summary>
/// Tax areas in memory: 401 NYC (NY, 8.875 from 2026-01-01) and 402 LA (CA, 9.5 from 2026-01-01 and 10.25 from 2099-01-01), which a
/// tax code uses. Ids avoid 1 and 2, which the translation fake answers for every target.
/// </summary>
public sealed class InMemoryTaxAreaRepository : ITaxAreaRepository
{
    public const long NycId = 401;
    public const long LaId = 402;

    public static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _lock = new();
    private readonly Dictionary<long, (TaxAreaRecord Row, List<TaxAreaRate> Rates)> _areas = new()
    {
        [NycId] = (new(NycId, "NYC", "New York City", "NY", Created), [new(new DateOnly(2026, 1, 1), 8.875m)]),
        [LaId] = (new(LaId, "LA", "Los Angeles", "CA", Created), [new(new DateOnly(2026, 1, 1), 9.5m), new(new DateOnly(2099, 1, 1), 10.25m)]),
    };

    private readonly HashSet<long> _usedByTaxCodes = [LaId];
    private long _nextId = 410;

    public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<TaxAreaSummaryRecord>> SearchAsync(
        string? keyword, string? stateCode, DateOnly today, int limit, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<TaxAreaSummaryRecord> rows = _areas.Values
                .Where(a => keyword is null || a.Row.TaxAreaCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || a.Row.TaxAreaName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Where(a => stateCode is null || a.Row.StateCode == stateCode)
                .OrderBy(a => a.Row.TaxAreaCode, StringComparer.Ordinal)
                .Take(limit)
                .Select(a => (a.Row, Current: a.Rates.Where(r => r.EffectiveFrom <= today).MaxBy(r => r.EffectiveFrom)))
                .Select(a => new TaxAreaSummaryRecord(
                    a.Row.Id, a.Row.TaxAreaCode, a.Row.TaxAreaName, a.Row.StateCode, a.Current?.EffectiveFrom, a.Current?.Rate))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<TaxAreaRecord?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_areas.TryGetValue(id, out var area) ? area.Row : null);
        }
    }

    public Task<IReadOnlyList<TaxAreaRate>> FindRatesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<TaxAreaRate>>(_areas.TryGetValue(id, out var area) ? [.. area.Rates.OrderBy(r => r.EffectiveFrom)] : []);
        }
    }

    public Task<long?> FindIdByCodeAsync(string taxAreaCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_areas.Values.Where(a => a.Row.TaxAreaCode == taxAreaCode).Select(a => (long?)a.Row.Id).FirstOrDefault());
        }
    }

    public Task<bool> IsUsedByTaxCodesAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(_usedByTaxCodes.Contains(id));

    public Task<long> InsertAsync(TaxAreaFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var id = _nextId++;
            _areas[id] = (new(id, fields.TaxAreaCode, fields.TaxAreaName, fields.StateCode, Created), []);
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        long id, TaxAreaFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_areas.TryGetValue(id, out var area) || area.Row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _areas[id] = (new(id, fields.TaxAreaCode, fields.TaxAreaName, fields.StateCode, expectedUpdatedAt.AddSeconds(1)), area.Rates);
            return Task.FromResult(true);
        }
    }

    public Task SaveRatesAsync(long id, TaxAreaFields fields, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _areas[id] = (_areas[id].Row, [.. fields.Rates]);
            return Task.CompletedTask;
        }
    }

    public Task<bool> DeleteAsync(long id, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_areas.TryGetValue(id, out var area) && area.Row.UpdatedAt == expectedUpdatedAt && _areas.Remove(id));
        }
    }
}
