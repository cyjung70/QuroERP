using System.Text;

namespace QuroErp.Infrastructure.Reports;

/// <summary>
/// IBM Plex Sans KR and Mono (SIL OFL-1.1, Reports/Fonts/OFL.txt) as @font-face rules with data URIs, so the page needs no
/// network request (reports.md "격리"). The files ship with the app and are read once.
/// </summary>
public static class PrintFonts
{
    /// <summary>Font family of the body text.</summary>
    public const string Sans = "IBM Plex Sans KR";

    /// <summary>Font family of numbers and codes.</summary>
    public const string Mono = "IBM Plex Mono";

    private static readonly Lazy<string> CssValue = new(Build);

    /// <summary>The style element with the font faces, placed in the head of every printed page.</summary>
    public static string StyleElement => CssValue.Value;

    /// <summary>Folder of the font files next to the app.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Reports", "Fonts");

    private static string Build()
    {
        var css = new StringBuilder("<style>");
        Face(css, Sans, "IBMPlexSansKR-Regular.ttf", 400);
        Face(css, Sans, "IBMPlexSansKR-Bold.ttf", 700);
        Face(css, Mono, "IBMPlexMono-Regular.ttf", 400);
        return css.Append("</style>").ToString();
    }

    private static void Face(StringBuilder css, string family, string file, int weight) =>
        css.Append("@font-face{font-family:'").Append(family).Append("';font-weight:").Append(weight)
            .Append(";font-style:normal;src:url(data:font/ttf;base64,")
            .Append(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(Folder, file))))
            .Append(") format('truetype');}");
}
