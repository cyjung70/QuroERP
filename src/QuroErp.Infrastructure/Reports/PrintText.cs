using System.Globalization;
using System.Resources;

namespace QuroErp.Infrastructure.Reports;

/// <summary>
/// Labels of printed documents in the user's language (PrintResource.resx ko default, PrintResource.en.resx). Templates use
/// resource keys only; a missing key prints the key so it shows up in review.
/// </summary>
public static class PrintText
{
    private static readonly ResourceManager Resources = new("QuroErp.Infrastructure.Reports.PrintResource", typeof(PrintText).Assembly);

    public static string Get(string key, CultureInfo culture) => Resources.GetString(key, culture) ?? key;
}
