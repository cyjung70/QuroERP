using System.Data;
using QuroErp.Application.Bc.Tax;
using QuroErp.Application.Common.I18n;
using QuroErp.Domain.Bc.Tax;

namespace QuroErp.Api.Tests.Bc.Tax;

/// <summary>
/// The KR company's codes: NONE (301), VAT10 (302, used by a document line) and VAT0 (303). Names are served through the factory's
/// translation fake.
/// </summary>
public sealed class InMemoryTaxCodeRepository : ITaxCodeRepository
{
    public const long NoneId = 301;
    public const long Vat10Id = 302;
    public const long Vat0Id = 303;

    public static readonly DateTimeOffset Created = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly Dictionary<long, TaxCodeRecord> _rows = new()
    {
        [NoneId] = new(NoneId, "NONE", TaxKind.None, 0, null, null, false, true, Created),
        [Vat10Id] = new(Vat10Id, "VAT10", TaxKind.VatStandard, 10, null, null, false, true, Created),
        [Vat0Id] = new(Vat0Id, "VAT0", TaxKind.VatZero, 0, null, null, false, true, Created),
    };
    private readonly Dictionary<long, Dictionary<string, string>> _names = new()
    {
        [NoneId] = new() { ["en"] = "No tax", ["ko"] = "세금 없음" },
        [Vat10Id] = new() { ["en"] = "VAT 10%", ["ko"] = "과세 10%" },
        [Vat0Id] = new() { ["en"] = "Zero rate", ["ko"] = "영세율" },
    };
    private readonly HashSet<long> _used = [Vat10Id];
    private long _nextId = 900;

    /// <summary>Translations of the codes held here, for the factory's translation fake.</summary>
    public IReadOnlyList<TranslationRow> NamesOf(IReadOnlyCollection<long> ids)
    {
        lock (_gate)
        {
            return ids.SelectMany(id => _names.GetValueOrDefault(id, []).Select(n => new TranslationRow(id, n.Key, n.Value))).ToList();
        }
    }

    public Task<IReadOnlyList<TaxCodeRecord>> FindAllAsync(bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<TaxCodeRecord>>(_rows.Values.OrderBy(r => r.TaxCode, StringComparer.Ordinal).ToList());
        }
    }

    public Task<IReadOnlySet<long>> FindUsedByDocumentsAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(ids.Where(_used.Contains).ToHashSet());

    /// <summary>No partner has these codes as default.</summary>
    public Task<IReadOnlySet<long>> FindUsedByPartnersAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());

    /// <summary>A KR company has no tax areas.</summary>
    public Task<IReadOnlySet<long>> FindTaxAreasAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());

    public Task<long> InsertAsync(string taxCode, TaxCodeFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var id = _nextId++;
            _rows[id] = new(id, taxCode, fields.TaxKind, fields.Rate, fields.TaxAreaId, null, fields.PriceIncluded, fields.UseYn, Created.AddDays(1));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        long id, string taxCode, TaxCodeFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_rows[id].UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = new(id, taxCode, fields.TaxKind, fields.Rate, fields.TaxAreaId, null, fields.PriceIncluded, fields.UseYn, expectedUpdatedAt.AddSeconds(1));
            return Task.FromResult(true);
        }
    }

    public Task SaveNamesAsync(
        long taxCodeId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var stored = _names.TryGetValue(taxCodeId, out var current) ? current : _names[taxCodeId] = [];
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
