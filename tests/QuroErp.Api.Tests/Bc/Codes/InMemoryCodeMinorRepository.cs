using System.Data;
using QuroErp.Application.Bc.Codes;
using QuroErp.Application.Common.I18n;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Api.Tests.Bc.Codes;

/// <summary>UNIT with EA (Each/개, a constant code) and BOX (stored by an item); TERMS with NET30. Names are served through the factory's translation fake.</summary>
public sealed class InMemoryCodeMinorRepository : ICodeMinorRepository
{
    public const long EaId = 501;
    public const long BoxId = 502;
    public const long Net30Id = 503;

    public static readonly DateTimeOffset Created = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _majors = new(StringComparer.Ordinal) { [CodeMajors.Unit] = 1, ["TERMS"] = 2 };
    private readonly Dictionary<long, (long MajorId, CodeMinorRecord Row)> _rows = new()
    {
        [EaId] = (1, new CodeMinorRecord(EaId, "EA", 10, true, null, null, Created)),
        [BoxId] = (1, new CodeMinorRecord(BoxId, "BOX", 20, true, null, null, Created)),
        [Net30Id] = (2, new CodeMinorRecord(Net30Id, "NET30", 10, true, null, null, Created)),
    };
    private readonly Dictionary<long, Dictionary<string, string>> _names = new()
    {
        [EaId] = new() { ["en"] = "Each", ["ko"] = "개" },
        [BoxId] = new() { ["en"] = "Box" },
        [Net30Id] = new() { ["en"] = "Net 30" },
    };
    private readonly HashSet<string> _used = new(StringComparer.Ordinal) { "BOX" };
    private long _nextId = 600;

    /// <summary>Translations of the Minor codes held here, for the factory's translation fake.</summary>
    public IReadOnlyList<TranslationRow> NamesOf(IReadOnlyCollection<long> ids)
    {
        lock (_gate)
        {
            return ids.SelectMany(id => _names.GetValueOrDefault(id, []).Select(n => new TranslationRow(id, n.Key, n.Value))).ToList();
        }
    }

    public Task<long?> FindMajorIdAsync(string majorCode, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(_majors.TryGetValue(majorCode, out var id) ? id : (long?)null);

    public Task<IReadOnlyList<CodeMinorRecord>> FindByMajorAsync(long majorId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CodeMinorRecord>>(
                _rows.Values.Where(r => r.MajorId == majorId).Select(r => r.Row).OrderBy(r => r.SortOrder).ThenBy(r => r.MinorCode).ToList());
        }
    }

    public Task<IReadOnlySet<string>> FindUsedAsync(
        string majorCode, IReadOnlyCollection<string> minorCodes, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(minorCodes.Where(_used.Contains).ToHashSet());

    public Task<long> InsertAsync(
        long majorId, string minorCode, CodeMinorFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var id = _nextId++;
            _rows[id] = (majorId, new CodeMinorRecord(id, minorCode, fields.SortOrder, fields.UseYn, fields.Attr1, fields.Attr2, Created.AddDays(1)));
            return Task.FromResult(id);
        }
    }

    public Task<bool> UpdateAsync(
        long id, CodeMinorFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var (majorId, row) = _rows[id];
            if (row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = (majorId, row with
            {
                SortOrder = fields.SortOrder,
                UseYn = fields.UseYn,
                Attr1 = fields.Attr1,
                Attr2 = fields.Attr2,
                UpdatedAt = Created.AddDays(1),
            });
            return Task.FromResult(true);
        }
    }

    public Task SaveNamesAsync(
        long minorId, IReadOnlyDictionary<string, string?> names, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var stored = _names.TryGetValue(minorId, out var current) ? current : _names[minorId] = [];
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
