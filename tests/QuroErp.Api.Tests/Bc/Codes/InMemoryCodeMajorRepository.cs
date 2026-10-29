using System.Data;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Common.I18n;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Api.Tests.Bc.Codes;

/// <summary>
/// UNIT (a constant Major with 2 Minor codes), TERMS (1 Minor code) and TEMP (none). Names are served through the factory's translation fake.
/// </summary>
public sealed class InMemoryCodeMajorRepository : ICodeMajorRepository
{
    public const long UnitId = 301;
    public const long TermsId = 302;
    public const long TempId = 303;

    /// <summary>Text of the database error that an insert of <see cref="FailingCode"/> throws. No response may show it (SD-BR-905).</summary>
    public const string DatabaseError = "duplicate key value violates unique constraint \"uq_sys_code_major_major_code\"";

    public static readonly DateTimeOffset Created = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly Dictionary<long, CodeMajorRecord> _rows = new()
    {
        [UnitId] = new CodeMajorRecord(UnitId, CodeMajors.Unit, true, 2, Created),
        [TermsId] = new CodeMajorRecord(TermsId, "TERMS", true, 1, Created),
        [TempId] = new CodeMajorRecord(TempId, "TEMP", true, 0, Created),
    };
    private readonly Dictionary<long, Dictionary<string, string>> _names = new()
    {
        [UnitId] = new() { ["en"] = "Unit", ["ko"] = "단위" },
        [TermsId] = new() { ["en"] = "Terms" },
        [TempId] = new() { ["en"] = "Temporary" },
    };
    private long _nextId = 400;

    /// <summary>A Major code whose insert fails as the database would on a unique key clash from a concurrent save.</summary>
    public string? FailingCode { get; set; }

    /// <summary>Translations of the Major codes held here, for the factory's translation fake.</summary>
    public IReadOnlyList<TranslationRow> NamesOf(IReadOnlyCollection<long> ids)
    {
        lock (_gate)
        {
            return ids.SelectMany(id => _names.GetValueOrDefault(id, []).Select(n => new TranslationRow(id, n.Key, n.Value))).ToList();
        }
    }

    public Task<IReadOnlyList<CodeMajorRecord>> FindAsync(
        string? codePrefix, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CodeMajorRecord>>(_rows.Values
                .Where(r => codePrefix is null || r.MajorCode.StartsWith(codePrefix, StringComparison.Ordinal))
                .OrderBy(r => r.MajorCode, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<long> InsertAsync(string majorCode, bool useYn, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (majorCode == FailingCode)
        {
            throw new InvalidOperationException(DatabaseError);
        }
        lock (_gate)
        {
            var id = _nextId++;
            _rows[id] = new CodeMajorRecord(id, majorCode, useYn, 0, Created.AddDays(1));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        long id, bool useYn, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var row = _rows[id];
            if (row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = row with { UseYn = useYn, UpdatedAt = Created.AddDays(1) };
            return Task.FromResult(true);
        }
    }

    public Task SaveNamesAsync(
        long majorId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var stored = _names.TryGetValue(majorId, out var current) ? current : _names[majorId] = [];
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
        }
        return Task.CompletedTask;
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
        }
        return Task.CompletedTask;
    }
}
