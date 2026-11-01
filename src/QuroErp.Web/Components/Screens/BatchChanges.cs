using QuroErp.Contracts.Common;

namespace QuroErp.Web.Components.Screens;

/// <summary>Changed rows of a grid, grouped by state.</summary>
public sealed record BatchChanges<TRow>(IReadOnlyList<TRow> Added, IReadOnlyList<TRow> Updated, IReadOnlyList<TRow> Deleted)
{
    public bool IsEmpty => Added.Count == 0 && Updated.Count == 0 && Deleted.Count == 0;

    /// <summary>The M screen batch request: deleted rows are sent as keys.</summary>
    public BatchRequest<TRow, TKey> ToRequest<TKey>(Func<TRow, TKey> key) => new(Added, Updated, [.. Deleted.Select(key)]);
}
