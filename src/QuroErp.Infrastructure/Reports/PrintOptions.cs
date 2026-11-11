using Microsoft.Extensions.Configuration;

namespace QuroErp.Infrastructure.Reports;

/// <summary>PDF conversion limits (reports.md "브라우저"): configuration section Print.</summary>
public sealed record PrintOptions(int MaxConcurrency = 4, int TimeoutSeconds = 30)
{
    /// <summary>Print:MaxConcurrency and Print:TimeoutSeconds; missing or not positive values keep the defaults.</summary>
    public static PrintOptions From(IConfiguration configuration)
    {
        var defaults = new PrintOptions();
        return new PrintOptions(
            Positive(configuration["Print:MaxConcurrency"]) ?? defaults.MaxConcurrency,
            Positive(configuration["Print:TimeoutSeconds"]) ?? defaults.TimeoutSeconds);
    }

    private static int? Positive(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
}
