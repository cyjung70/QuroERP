using System.Globalization;
using Microsoft.AspNetCore.Components;
using QuroErp.Web.Common;

namespace QuroErp.Web.Components.Screens;

/// <summary>
/// A grid column. Declared inside a grid's <c>Columns</c>; renders nothing itself, the grid renders the cells.
/// Without <see cref="ChildContent"/> the cell shows <see cref="Value"/>; with it the cell is an editor.
/// </summary>
public sealed class QuroColumn<TRow> : ComponentBase
    where TRow : class
{
    [CascadingParameter]
    public QuroColumnSet<TRow>? Columns { get; set; }

    /// <summary>Translated column title.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    /// <summary>Display and Excel value.</summary>
    [Parameter]
    public Func<TRow, object?>? Value { get; set; }

    /// <summary>Format of an IFormattable value, e.g. N2. Without it a date shows in the user language's date pattern.</summary>
    [Parameter]
    public string? Format { get; set; }

    /// <summary>A numeric column totalled in the footer row of a read-only list (lookup dialog, sub-grid).</summary>
    [Parameter]
    public bool Sum { get; set; }

    /// <summary>Cell editor, e.g. a QuroText with HideLabel.</summary>
    [Parameter]
    public RenderFragment<TRow>? ChildContent { get; set; }

    protected override void OnInitialized() => Columns?.Add(this);

    internal string Text(TRow row) => Value?.Invoke(row) switch
    {
        null => "",
        DateOnly date when Format is null => DisplayCulture.FormatDate(date),
        DateTime at when Format is null => DisplayCulture.FormatDate(at),
        DateTimeOffset at when Format is null => DisplayCulture.FormatDate(at.DateTime),
        IFormattable formattable => formattable.ToString(Format, CultureInfo.CurrentCulture),
        var value => value.ToString() ?? "",
    };

    internal RenderFragment Cell(TRow row) => ChildContent?.Invoke(row) ?? (builder => builder.AddContent(0, Text(row)));
}

/// <summary>Columns registered by the <see cref="QuroColumn{TRow}"/> children of a grid.</summary>
public sealed class QuroColumnSet<TRow>
    where TRow : class
{
    private readonly List<QuroColumn<TRow>> _columns = [];

    public IReadOnlyList<QuroColumn<TRow>> Items => _columns;

    internal void Add(QuroColumn<TRow> column)
    {
        if (!_columns.Contains(column))
        {
            _columns.Add(column);
        }
    }
}
