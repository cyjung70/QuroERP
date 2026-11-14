namespace QuroErp.Application.Common.Printing;

/// <summary>
/// Paper of a printed document (reports.md "용지"): US Letter for a US company, A4 for a KR company, 12 mm margins and the page
/// number footer <c>n / N</c>.
/// </summary>
public sealed record PrintPaper(string Format)
{
    /// <summary>Margin on every side.</summary>
    public const string Margin = "12mm";

    public static PrintPaper Letter { get; } = new("Letter");

    public static PrintPaper A4 { get; } = new("A4");

    /// <summary>A4 for KR; Letter for US and when the company country is not set (the default country is US).</summary>
    public static PrintPaper ForCountry(string? countryCode) =>
        string.Equals(countryCode, "KR", StringComparison.OrdinalIgnoreCase) ? A4 : Letter;

    /// <summary>Page width of a label (CSS length, e.g. "100mm"); null for a paper format. A label has no margin and no footer.</summary>
    public string? Width { get; init; }

    /// <summary>Page height of a label; set with <see cref="Width"/>.</summary>
    public string? Height { get; init; }

    /// <summary>A label page of widthMm × heightMm (MM-BR-917), without margins or page number.</summary>
    public static PrintPaper Label(decimal widthMm, decimal heightMm) => new("")
    {
        Width = widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "mm",
        Height = heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "mm",
    };
}

/// <summary>
/// Company head of a printed document (print-documents.md): name, address lines and contacts from bc_company, logo from
/// bc_company.logo_image. Without a logo only the name is printed.
/// </summary>
public sealed record PrintCompany(
    string Name, IReadOnlyList<string> AddressLines, string? Phone, string? Email, byte[]? Logo = null, string? LogoContentType = null);

/// <summary>
/// HTML template → PDF (reports.md "PDF"). The template is a Razor component of the Infrastructure Reports/Templates folder;
/// the parameters are its component parameters, usually the document DTO. Values are HTML encoded, the page runs without
/// JavaScript and every network request is blocked.
/// </summary>
public interface IPdfRenderer
{
    /// <param name="templateType">A Razor component type (Microsoft.AspNetCore.Components.IComponent).</param>
    /// <exception cref="TimeoutException">The conversion took longer than the configured limit (default 30 seconds).</exception>
    Task<byte[]> RenderAsync(
        Type templateType, IReadOnlyDictionary<string, object?> parameters, PrintPaper paper, CancellationToken cancellationToken);

    /// <summary>Starts Chromium if needed and opens a blank page (health check).</summary>
    Task CheckAsync(CancellationToken cancellationToken);
}
