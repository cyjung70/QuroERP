using System.Text.RegularExpressions;
using QuroErp.Application.Common.Printing;

namespace QuroErp.Infrastructure.Reports;

/// <summary>
/// Reports/Custom/&lt;module&gt;/&lt;document&gt;.frx next to the app wins over the HTML template (reports.md "고객사 양식").
/// The FastReport package comes with the first customer template; until then a custom file is only found, not printed.
/// </summary>
public sealed partial class PrintTemplateResolver(string root) : IPrintTemplateResolver
{
    /// <summary>The Reports/Custom folder next to the app.</summary>
    public PrintTemplateResolver() : this(Path.Combine(AppContext.BaseDirectory, "Reports", "Custom"))
    {
    }

    public PrintTemplate Resolve(string moduleCode, string document)
    {
        // Names become a path: only lower-case letters, digits and hyphens, so nothing leaves the root folder
        if (!Name().IsMatch(moduleCode) || !Name().IsMatch(document))
        {
            throw new ArgumentException($"Invalid print template name '{moduleCode}/{document}'.");
        }
        var path = Path.Combine(root, moduleCode, document + ".frx");
        return File.Exists(path) ? new PrintTemplate(path) : PrintTemplate.Html;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Name();
}
