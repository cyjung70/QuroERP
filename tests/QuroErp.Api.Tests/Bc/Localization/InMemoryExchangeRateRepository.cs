using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Api.Tests.Bc.Localization;

/// <summary>USD rates of the KR company (base KRW): 2026-10-01 (201) and 2026-10-02 (202).</summary>
public sealed class InMemoryExchangeRateRepository : IExchangeRateRepository
{
    public const long Oct1Id = 201;
    public const long Oct2Id = 202;

    public static readonly DateTimeOffset Created = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly Dictionary<long, ExchangeRateRecord> _rows = new()
    {
        [Oct1Id] = new(Oct1Id, new DateOnly(2026, 10, 1), "USD", 1350m, Created),
        [Oct2Id] = new(Oct2Id, new DateOnly(2026, 10, 2), "USD", 1352.5m, Created),
    };
    private long _nextId = 800;

    public Task LockAsync(IDbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ExchangeRateRecord>> FindAsync(
        DateOnly first, DateOnly last, string? currencyCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ExchangeRateRecord>>(_rows.Values
                .Where(r => r.RateDt >= first && r.RateDt <= last && (currencyCode is null || r.CurrencyCode == currencyCode))
                .OrderByDescending(r => r.RateDt).ThenBy(r => r.CurrencyCode, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<IReadOnlyList<ExchangeRateRecord>> FindByIdsAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ExchangeRateRecord>>(ids.Where(_rows.ContainsKey).Select(id => _rows[id]).ToList());
        }
    }

    public Task<IReadOnlyList<ExchangeRateRecord>> FindByKeysAsync(
        IReadOnlyCollection<(DateOnly RateDt, string CurrencyCode)> keys, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ExchangeRateRecord>>(_rows.Values.Where(r => keys.Contains((r.RateDt, r.CurrencyCode))).ToList());
        }
    }

    public Task<decimal?> FindLatestAsync(string currencyCode, DateOnly onOrBefore, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_rows.Values.Where(r => r.CurrencyCode == currencyCode && r.RateDt <= onOrBefore)
                .OrderByDescending(r => r.RateDt).Select(r => (decimal?)r.Rate).FirstOrDefault());
        }
    }

    public Task<long> InsertAsync(ExchangeRateFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var id = _nextId++;
            _rows[id] = new(id, fields.RateDt, fields.CurrencyCode, fields.Rate, Created.AddDays(1));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        long id, ExchangeRateFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_rows[id].UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = new(id, fields.RateDt, fields.CurrencyCode, fields.Rate, expectedUpdatedAt.AddSeconds(1));
            return Task.FromResult(true);
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var id in ids)
            {
                _rows.Remove(id);
            }
            return Task.CompletedTask;
        }
    }
}
