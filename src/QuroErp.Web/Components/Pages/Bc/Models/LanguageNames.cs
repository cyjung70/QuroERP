using Quro.Grid.Definition;

namespace QuroErp.Web.Components.Pages.Bc.Models;

/// <summary>
/// Name columns per language on the package grid, which binds to properties only (TASK-CORE-057): a row keeps its names by
/// language and shows each language as a property and a definition column <c>name{Lang}</c> (<c>nameEn</c>, <c>nameKo</c>).
/// </summary>
public static class LanguageNames
{
    /// <summary>Languages that have a name column. Another language in use keeps its loaded names and sends them back unchanged.</summary>
    public static IReadOnlyList<string> WithColumn { get; } = ["en", "ko"];

    /// <summary>Sets a name. A language not in use gets no key, so a save does not send it (BC-BR-405).</summary>
    public static void Set(Dictionary<string, string?> names, string lang, string? value)
    {
        if (value is not null || names.ContainsKey(lang))
        {
            names[lang] = value;
        }
    }

    /// <summary>The grid with the name columns of languages not in use hidden.</summary>
    public static GridDef InUse(GridDef grid, IReadOnlyCollection<string> languages) => grid with
    {
        Columns = [.. grid.Columns.Select(c => LangOf(c.Id) is { } lang && !languages.Contains(lang) ? c with { Hidden = true, Req = false } : c)],
    };

    private static string? LangOf(string columnId) =>
        WithColumn.FirstOrDefault(l => string.Equals(columnId, "name" + char.ToUpperInvariant(l[0]) + l[1..], StringComparison.Ordinal));
}
