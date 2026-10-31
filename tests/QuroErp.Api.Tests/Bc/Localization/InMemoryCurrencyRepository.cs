using System.Data;
using QuroErp.Application.Bc.Localization;
using QuroErp.Application.Common.I18n;
using QuroErp.Domain.Bc.Localization;

namespace QuroErp.Api.Tests.Bc.Localization;

/// <summary>
/// KRW (101, 대한민국 원/Korean Won) and USD (102, US Dollar, stored by a document), the same ids as the localization fake.
/// Names are served through the factory's translation fake.
/// </summary>
public sealed class InMemoryCurrencyRepository : ICurrencyRepository
{
    public const long KrwId = 101;
    public const long UsdId = 102;

    public static readonly DateTimeOffset Created = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly Dictionary<long, CurrencyRecord> _rows = new()
    {
        [KrwId] = new CurrencyRecord(KrwId, "KRW", 0, "KR", true, Created),
        [UsdId] = new CurrencyRecord(UsdId, "USD", 2, "US", true, Created),
    };
    private readonly Dictionary<long, Dictionary<string, string>> _names = new()
    {
        [KrwId] = new() { ["ko"] = "대한민국 원", ["en"] = "Korean Won" },
        [UsdId] = new() { ["en"] = "US Dollar" },
    };
    private readonly HashSet<string> _used = new(StringComparer.Ordinal) { "USD" };
    private long _nextId = 700;

    /// <summary>Translations of the currencies held here, for the factory's translation fake.</summary>
    public IReadOnlyList<TranslationRow> NamesOf(IReadOnlyCollection<long> ids)
    {
        lock (_gate)
        {
            return ids.SelectMany(id => _names.GetValueOrDefault(id, []).Select(n => new TranslationRow(id, n.Key, n.Value))).ToList();
        }
    }

    public Task<IReadOnlyList<CurrencyRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CurrencyRecord>>(_rows.Values.OrderBy(r => r.CurrencyCode, StringComparer.Ordinal).ToList());
        }
    }

    public Task<IReadOnlySet<string>> FindUsedByDocumentsAsync(
        IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(currencyCodes.Where(_used.Contains).ToHashSet(StringComparer.Ordinal));

    /// <summary>No partner trades in these currencies.</summary>
    public Task<IReadOnlySet<string>> FindUsedByPartnersAsync(
        IReadOnlyCollection<string> currencyCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

    public Task<long> InsertAsync(string currencyCode, CurrencyFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var id = _nextId++;
            _rows[id] = new CurrencyRecord(id, currencyCode, fields.Decimals, fields.CountryCode, fields.UseYn, Created.AddDays(1));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        long id, CurrencyFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var row = _rows[id];
            if (row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = row with { Decimals = fields.Decimals, CountryCode = fields.CountryCode, UseYn = fields.UseYn, UpdatedAt = row.UpdatedAt.AddSeconds(1) };
            return Task.FromResult(true);
        }
    }

    public Task SaveNamesAsync(
        long currencyId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var stored = _names.TryGetValue(currencyId, out var current) ? current : _names[currencyId] = [];
            foreach (var (lang, name) in names)
            {
                if (name is null)
                {
                    stored.Remove(lang);
                }
                else
                {
                    stored[lang] = name;
                }
            }
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var id in ids)
            {
                _rows.Remove(id);
                _names.Remove(id);
            }
            return Task.CompletedTask;
        }
    }
}
