using System.Text.RegularExpressions;
using Quro.Grid.Data;
using Quro.Grid.Definition;
using QuroErp.Web.Common;

namespace QuroErp.Web.Components.Screens;

/// <summary>
/// Lookup rules and popup cells of a definition grid (qurogrid-integration.md 4 "코드도움"), shared by the line grid of SM·MM screens
/// and the M grid: codes are read through the screen's lookup, found codes fill the rule's target columns, others are marked.
/// </summary>
public static partial class GridLookups
{
    /// <summary>The error mark of a code the lookup did not find; saving waits until it is fixed.</summary>
    public const string CodeNotFound = "Common.CodeNotFound";

    [GeneratedRegex(@"^\s*(?<lookup>[A-Za-z][A-Za-z0-9]*)\.(?<query>[A-Za-z][A-Za-z0-9]*)\((?<args>[^)]*)\)\s*$")]
    private static partial Regex Call();

    /// <summary>The lookup rules of a grid, e.g. <c>itemCd.change, lookup, itemName, "items.all()"</c>.</summary>
    public static IReadOnlyList<RuleDef> RulesOf(GridDef grid) => [.. grid.Rules.Where(r => r.Action == RuleAction.Lookup)];

    /// <summary>The field whose change runs a lookup rule.</summary>
    public static string TriggerOf(RuleDef rule) => rule.On[..^".change".Length];

    /// <summary>Codes in the grid's columns that the lookup did not find, on rows not marked deleted.</summary>
    public static bool HasCodeErrors<TRow>(QuroDataSet<TRow> data, GridDef grid)
        where TRow : class =>
        data.RowKeys.Where(k => data.GetState(k) != RowState.Deleted)
            .Any(k => grid.Columns.Any(c => data.GetCellMark(k, c.Id) is { Level: CellMarkLevel.Error }));

    /// <summary>
    /// One call for all codes of the rule; found codes fill the target columns of the same id, others are marked.
    /// </summary>
    /// <param name="headerValue">Values of the query arguments by header field, for a rule such as <c>items.byVendor(vendorCode)</c>.</param>
    /// <returns>The cells filled, so the caller can evaluate the calculations that read them.</returns>
    public static async Task<List<(int RowKey, string Field)>> FillAsync<TRow>(
        QuroDataSet<TRow> data, GridDef grid, RuleDef rule, IReadOnlyList<int> rowKeys,
        Func<LookupQuery, Task<IReadOnlyList<LookupRow>>> lookup, Func<string, object?>? headerValue = null)
        where TRow : class
    {
        var codeField = TriggerOf(rule);
        var filled = new List<(int RowKey, string Field)>();
        var call = Call().Match(rule.Expr);
        var lookupId = call.Success ? call.Groups["lookup"].Value : ColumnOf(grid, codeField)?.Lookup ?? "";
        var definition = ScreenDefinitions.Shared.Lookup(lookupId);
        var codes = rowKeys.Select(k => data.GetValue(k, codeField) as string).Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
        var args = call.Success
            ? call.Groups["args"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(a => headerValue?.Invoke(a)).ToList()
            : [];
        var rows = codes.Count == 0
            ? []
            : await lookup(new LookupQuery(lookupId, null, codes, call.Success ? call.Groups["query"].Value : null, args));
        var byCode = new Dictionary<string, LookupRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row[definition.Key]?.ToString() is { } code)
            {
                byCode.TryAdd(code, row);
            }
        }
        foreach (var rowKey in rowKeys)
        {
            var code = data.GetValue(rowKey, codeField) as string;
            if (string.IsNullOrEmpty(code))
            {
                data.SetCellMark(rowKey, codeField, null!);
                continue;
            }
            if (!byCode.TryGetValue(code, out var found))
            {
                data.SetCellMark(rowKey, codeField, new CellMark(CellMarkLevel.Error, CodeNotFound));
                continue;
            }
            data.SetCellMark(rowKey, codeField, null!);
            foreach (var target in rule.Targets.Where(found.Has))
            {
                data.SetValue(rowKey, target, found[target]);
                filled.Add((rowKey, target));
            }
        }
        return filled;
    }

    /// <summary>The popup cell's typed text: one match is taken, several open the dialog, none keeps the cell in edit (A-05).</summary>
    public static async Task<IReadOnlyList<string>> CodesAsync(
        Func<LookupQuery, Task<IReadOnlyList<LookupRow>>>? lookup, string lookupId, string text)
    {
        if (lookup is null)
        {
            return [];
        }
        var key = ScreenDefinitions.Shared.Lookup(lookupId).Key;
        var rows = await lookup(new LookupQuery(lookupId, text));
        return [.. rows.Select(r => r[key]?.ToString()).Where(c => c is not null).Cast<string>()];
    }

    public static ColumnDef? ColumnOf(GridDef grid, string field) =>
        grid.Columns.FirstOrDefault(c => string.Equals(c.Id, field, StringComparison.OrdinalIgnoreCase));

    public static string? FormatOf(LookupColumnDef column) => string.IsNullOrEmpty(column.Fmt) ? null : column.Fmt;
}
