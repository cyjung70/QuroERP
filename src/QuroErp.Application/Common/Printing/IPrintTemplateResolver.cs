namespace QuroErp.Application.Common.Printing;

/// <summary>Which template prints a document: the standard HTML template, or the customer's FastReport file at CustomPath.</summary>
public sealed record PrintTemplate(string? CustomPath)
{
    public static PrintTemplate Html { get; } = new((string?)null);

    public bool IsCustom => CustomPath is not null;
}

/// <summary>
/// Picks the template of a document (reports.md "고객사 양식"): Reports/Custom/&lt;module&gt;/&lt;document&gt;.frx when the
/// customer has one, else the standard HTML template.
/// </summary>
public interface IPrintTemplateResolver
{
    /// <param name="moduleCode">Module in lower case, e.g. mm.</param>
    /// <param name="document">Document name of print-documents.md, e.g. purchase-order.</param>
    /// <exception cref="ArgumentException">The module or document name has characters other than a-z, 0-9 and -.</exception>
    PrintTemplate Resolve(string moduleCode, string document);
}
