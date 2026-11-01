using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// Page object of a QuroGrid package grid in a screen frame (TASK-CORE-055). The package edits cells and clicks cell buttons
/// in its browser script, so the tests call the same grid entry points the script calls. Rows are found by
/// <c>data-row</c> (the row key) in display order: the package has no row test ids yet (TODO(QuroGrid-R3)).
/// </summary>
public sealed class DataGridPage<TRow>(
    Func<string, IReadOnlyList<IElement>> findAll, Func<IRenderedComponent<Quro.Grid.QuroGrid<TRow>>> grid, string testId)
    where TRow : class
{
    private IRenderedComponent<Quro.Grid.QuroGrid<TRow>> Grid => grid();

    public IReadOnlyList<IElement> Rows => findAll($"[data-testid={testId}] .quro-body [role=row][data-row]");

    public int KeyOf(int index) => int.Parse(Rows[index].GetAttribute("data-row")!, System.Globalization.CultureInfo.InvariantCulture);

    public string Text(int index, string field) => Rows[index].QuerySelector($"[data-field={field}]")!.TextContent.Trim();

    public IReadOnlyList<string> Column(string field) => [.. Rows.Select(r => r.QuerySelector($"[data-field={field}]")!.TextContent.Trim())];

    public bool IsReadOnly(int index, string field) =>
        Rows[index].QuerySelector($"[data-field={field}]")!.GetAttribute("aria-readonly") == "true";

    /// <summary>Types a cell value and leaves the cell, as the browser script does. False when the grid did not take it.</summary>
    public bool Edit(int index, string field, string? text)
    {
        var key = KeyOf(index);
        var cut = Grid;
        return cut.InvokeAsync(() => cut.Instance.CommitEdit(key, field, text)).GetAwaiter().GetResult();
    }

    /// <summary>Clicks the button cell of a row.</summary>
    public void Click(int index, string field)
    {
        var key = KeyOf(index);
        var cut = Grid;
        cut.InvokeAsync(() => cut.Instance.ClickButton(key, field)).GetAwaiter().GetResult();
    }
}

public static class DataGridPageExtensions
{
    public static DataGridPage<TRow> DataGrid<TRow, TPage>(this IRenderedComponent<TPage> page, string testId)
        where TRow : class
        where TPage : class, IComponent =>
        new(selector => page.FindAll(selector), () => page.FindComponent<Quro.Grid.QuroGrid<TRow>>(), testId);
}
