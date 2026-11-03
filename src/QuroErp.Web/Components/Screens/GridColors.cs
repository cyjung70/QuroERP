using System.Text;
using Quro.Grid.Definition;

namespace QuroErp.Web.Components.Screens;

/// <summary>
/// Cell colors of an editable grid (entry-frame.md 5): editable white, read-only and computed gray (quro-entry.css), required light
/// yellow. The package marks read-only cells (aria-readonly) but not required ones, so the grid host gets a style naming its required
/// columns from the definition after the field policies. TODO(QuroGrid-R5): drop the style once body cells carry a required mark.
/// </summary>
public static class GridColors
{
    /// <summary>A new element id for one grid host, so its required-column style touches only that grid.</summary>
    public static string NewHostId() => $"quro-grid-{Guid.NewGuid():N}";

    /// <summary>The ids of the columns that are required and editable on the grid's rows, in definition order.</summary>
    public static IReadOnlyList<string> RequiredColumns(GridDef? grid) =>
        grid is null ? [] : [.. grid.Columns.Where(c => c.Req && c.Edit != ColumnEdit.ReadOnly && !c.Hidden).Select(c => c.Id)];

    /// <summary>
    /// CSS that gives the required columns' editable cells the required background, or null when the grid has none. Error and
    /// warning marks keep their own colors.
    /// </summary>
    public static string? RequiredStyle(string hostId, GridDef? grid)
    {
        var columns = RequiredColumns(grid);
        if (columns.Count == 0)
        {
            return null;
        }
        var css = new StringBuilder();
        css.AppendJoin(", ", columns.Select(c =>
            $"#{hostId} .quro-body-cell[data-field=\"{c}\"]:not([aria-readonly=true], .quro-mark-error, .quro-mark-warning)"));
        css.Append(" { background: var(--q-field-required-bg); }");
        return css.ToString();
    }
}
