namespace QuroErp.Contracts.Bc.Tax;

/// <summary>US tax area API routes (BC-UC-029).</summary>
public static class TaxAreaRoutes
{
    /// <summary>GET list (query <see cref="TaxAreaSearchQuery"/>), POST new area; GET, PUT, DELETE {Base}/{id}.</summary>
    public const string Base = "api/bc/tax-areas";

    /// <summary>Most rows the list returns, in code order.</summary>
    public const int MaxListSize = 200;
}

/// <summary>List and find conditions; all optional. Keyword matches part of the code or name, ignoring case.</summary>
public sealed record TaxAreaSearchQuery(string? Keyword, string? StateCode);

/// <summary>A list row with the rate valid today in the company time zone; both are null when no line has started yet.</summary>
public sealed record TaxAreaSummaryDto(
    long Id, string TaxAreaCode, string TaxAreaName, string StateCode, DateOnly? EffectiveFrom, decimal? Rate);

/// <summary>One combined rate (state, county, city and district) from a start date. Rate is a percentage.</summary>
public sealed record TaxAreaRateDto(DateOnly EffectiveFrom, decimal Rate);

/// <summary>One tax area with its rate lines in start date order.</summary>
public sealed record TaxAreaDto(
    long Id, string TaxAreaCode, string TaxAreaName, string StateCode, IReadOnlyList<TaxAreaRateDto> Rates, DateTimeOffset UpdatedAt);

/// <summary>A rate line to save; the server checks both parts (BC-BR-512).</summary>
public sealed record SaveTaxAreaRateRow(DateOnly? EffectiveFrom, decimal? Rate);

/// <summary>
/// New or changed tax area. The lines replace the stored ones: lines left out are deleted. UpdatedAt is required on PUT.
/// </summary>
public sealed record SaveTaxAreaRequest(
    string? TaxAreaCode, string? TaxAreaName, string? StateCode, IReadOnlyList<SaveTaxAreaRateRow>? Rates, DateTimeOffset? UpdatedAt);
