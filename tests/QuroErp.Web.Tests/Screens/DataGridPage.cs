using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Quro.Grid;

namespace QuroErp.Web.Tests.Screens;

/// <summary>
/// Page object of a QuroGrid package grid in a screen frame (TASK-CORE-055). The package edits cells and clicks cell buttons
/// in its browser script, so the tests call the same grid entry points the script calls. Rows are found by
/// <c>data-row</c> (the row key) in display order: the package has no row test ids yet (TODO(QuroGrid-R3)).
/// </summary>
public sealed class DataGridPage<TRow>(
    Func<string, IReadOnlyList<IElement>> findAll, Func<IRenderedComponent<QuroGrid<TRow>>> grid, string testId)
    where TRow : class
{
    private IRenderedComponent<QuroGrid<TRow>> Grid => grid();

    public IReadOnlyList<IElement> Rows => findAll($"[data-testid={testId}] .quro-body [role=row][data-row]");

    public int KeyOf(int index) => int.Parse(Rows[index].GetAttribute("data-row")!, System.Globalization.CultureInfo.InvariantCulture);

    public string Text(int index, string field) => Rows[index].QuerySelector($"[data-field={field}]")!.TextContent.Trim();

    public IReadOnlyList<string> Column(string field) => [.. Rows.Select(r => r.QuerySelector($"[data-field={field}]")!.TextContent.Trim())];

    /// <summary>The shown column headers in order, as field and title.</summary>
    public IReadOnlyList<(string Field, string Title)> Headers =>
        [.. findAll($"[data-testid={testId}] .quro-head [role=columnheader][data-field]")
            .Select(h => (h.GetAttribute("data-field")!, h.TextContent.Trim()))];

    /// <summary>Whether the check cell of a row is checked.</summary>
    public bool IsChecked(int index, string field) =>
        Rows[index].QuerySelector($"[data-field={field}] .quro-check")!.GetAttribute("aria-checked") == "true";

    /// <summary>Clicks a check cell, as the browser script does. False when the grid did not take it.</summary>
    public bool Toggle(int index, string field)
    {
        var key = KeyOf(index);
        var cut = Grid;
        return cut.InvokeAsync(() => cut.Instance.ToggleCheck(key, field)).GetAwaiter().GetResult();
    }

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

    /// <summary>Makes a row the active row, as a click in the browser does.</summary>
    public void Select(int index)
    {
        var key = KeyOf(index);
        var cut = Grid;
        cut.InvokeAsync(() => cut.Instance.SelectionChanged(key)).GetAwaiter().GetResult();
    }

    /// <summary>Pastes Excel-style cells (rows × columns) from a cell, as the browser script does. False when cancelled.</summary>
    public bool Paste(int index, string field, params string[][] cells)
    {
        var key = KeyOf(index);
        var cut = Grid;
        return cut.InvokeAsync(() => cut.Instance.Paste(key, field, cells)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Types text in a popup cell and leaves it without waiting for the grid, which waits while a lookup dialog is open.
    /// </summary>
    public Task<bool> EditAsync(int index, string field, string? text)
    {
        var key = KeyOf(index);
        var cut = Grid;
        return cut.InvokeAsync(() => cut.Instance.CommitEdit(key, field, text));
    }

    /// <summary>Presses F4 in a popup cell with the typed text; the grid waits while the lookup dialog is open.</summary>
    public Task<bool> F4Async(int index, string field, string text)
    {
        var key = KeyOf(index);
        var cut = Grid;
        return cut.InvokeAsync(() => cut.Instance.RequestPopup(key, field, text));
    }
}

public static class DataGridPageExtensions
{
    /// <summary>
    /// The test id of an input, button or grid. Quro.UI inputs and package grids take none yet; their field or host element has
    /// it (TODO(QuroGrid-R3)).
    /// </summary>
    public static string? TestIdOf(IElement element) =>
        element.GetAttribute("data-testid")
        ?? (element.ClassList.Contains("quro-ui-input") || element.GetAttribute("role") == "grid"
            ? element.Closest("[data-testid]")?.GetAttribute("data-testid")
            : null);

    /// <summary>The result rows of a lookup dialog (its list is a package grid, TASK-CORE-056).</summary>
    public static IReadOnlyList<IElement> LookupRows<TPage>(this IRenderedComponent<TPage> page, string dialog)
        where TPage : class, IComponent =>
        page.FindAll($"[data-testid={dialog}-grid] .quro-body [role=row][data-row]");

    /// <summary>Clicks the select button of a lookup dialog row.</summary>
    public static void PickLookupRow<TItem, TPage>(this IRenderedComponent<TPage> page, string dialog, int index)
        where TItem : class
        where TPage : class, IComponent =>
        page.DataGrid<TItem, TPage>($"{dialog}-grid").Click(index, QuroErp.Web.Components.Screens.QuroListGrid<TItem>.SelectField);

    /// <summary>The package grid in the element with the test id; <c>index</c> picks among grids of the same row type.</summary>
    public static DataGridPage<TRow> DataGrid<TRow, TPage>(this IRenderedComponent<TPage> page, string testId, int index)
        where TRow : class
        where TPage : class, IComponent =>
        new(selector => page.FindAll(selector), () => page.FindComponents<QuroGrid<TRow>>()[index], testId);

    public static DataGridPage<TRow> DataGrid<TRow, TPage>(this IRenderedComponent<TPage> page, string testId)
        where TRow : class
        where TPage : class, IComponent =>
        new(selector => page.FindAll(selector), () => page.FindComponent<QuroGrid<TRow>>(), testId);
}
