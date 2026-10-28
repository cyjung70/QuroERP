using System.Text.Json;
using QuroErp.Contracts.Common;

namespace QuroErp.Web.Components.Screens;

/// <summary>Row state of a grid row (screens.md "그리드 행 상태").</summary>
public enum RowState
{
    Unchanged,

    /// <summary>I: new row.</summary>
    Added,

    /// <summary>U: loaded row whose values changed.</summary>
    Modified,

    /// <summary>D: loaded row marked for deletion.</summary>
    Deleted,
}

/// <summary>
/// A grid row and its state. A loaded row keeps a JSON snapshot of its values, so edits are detected
/// without the screen marking them.
/// </summary>
public sealed class GridRow<TRow>
    where TRow : class
{
    private readonly string? _original;

    internal GridRow(TRow item, bool isNew)
    {
        Item = item;
        IsNew = isNew;
        _original = isNew ? null : Snapshot.Of(item);
    }

    public TRow Item { get; private set; }

    public bool IsNew { get; }

    public bool IsDeleted { get; internal set; }

    public RowState State =>
        IsDeleted ? RowState.Deleted
        : IsNew ? RowState.Added
        : Snapshot.Of(Item) != _original ? RowState.Modified
        : RowState.Unchanged;

    /// <summary>A loaded row that was edited or marked deleted. A new row has no loaded values to go back to.</summary>
    public bool CanUndo => !IsNew && State != RowState.Unchanged;

    // The item is replaced by a copy of the loaded values, so the screen must read row.Item again
    internal void Undo()
    {
        Item = Snapshot.Restore<TRow>(_original!);
        IsDeleted = false;
    }
}

/// <summary>Rows of an editable grid with I/U/D tracking. Saving sends only the changed rows.</summary>
public sealed class GridRows<TRow>
    where TRow : class
{
    private readonly List<GridRow<TRow>> _rows = [];

    public IReadOnlyList<GridRow<TRow>> All => _rows;

    public bool IsDirty => _rows.Any(r => r.State != RowState.Unchanged);

    /// <summary>Replaces the rows with loaded rows (all unchanged).</summary>
    public void Load(IEnumerable<TRow> items)
    {
        _rows.Clear();
        _rows.AddRange(items.Select(item => new GridRow<TRow>(item, isNew: false)));
    }

    public void Clear() => _rows.Clear();

    public GridRow<TRow> Add(TRow item)
    {
        var row = new GridRow<TRow>(item, isNew: true);
        _rows.Add(row);
        return row;
    }

    /// <summary>
    /// Marks a loaded row deleted, or restores it. A new row is just removed: the server never saw it.
    /// </summary>
    public void ToggleDelete(GridRow<TRow> row)
    {
        if (row.IsNew)
        {
            _rows.Remove(row);
            return;
        }
        row.IsDeleted = !row.IsDeleted;
    }

    /// <summary>Puts back the loaded values of an edited or deleted row (Undo Row).</summary>
    public void Undo(GridRow<TRow> row)
    {
        if (row.CanUndo)
        {
            row.Undo();
        }
    }

    public BatchChanges<TRow> Changes() => new(
        [.. _rows.Where(r => r.State == RowState.Added).Select(r => r.Item)],
        [.. _rows.Where(r => r.State == RowState.Modified).Select(r => r.Item)],
        [.. _rows.Where(r => r.State == RowState.Deleted).Select(r => r.Item)]);
}

/// <summary>Changed rows of a grid, grouped by state.</summary>
public sealed record BatchChanges<TRow>(IReadOnlyList<TRow> Added, IReadOnlyList<TRow> Updated, IReadOnlyList<TRow> Deleted)
{
    public bool IsEmpty => Added.Count == 0 && Updated.Count == 0 && Deleted.Count == 0;

    /// <summary>The M screen batch request: deleted rows are sent as keys.</summary>
    public BatchRequest<TRow, TKey> ToRequest<TKey>(Func<TRow, TKey> key) => new(Added, Updated, [.. Deleted.Select(key)]);
}

/// <summary>Value snapshots for change detection.</summary>
internal static class Snapshot
{
    public static string Of<T>(T value) => JsonSerializer.Serialize(value);

    public static T Restore<T>(string snapshot) =>
        JsonSerializer.Deserialize<T>(snapshot) ?? throw new InvalidOperationException("Empty snapshot.");
}
