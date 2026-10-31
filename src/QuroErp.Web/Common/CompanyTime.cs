using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using QuroErp.Contracts.Common.Codes;
using QuroErp.Web.Bc.Localization;

namespace QuroErp.Web.Common;

/// <summary>
/// The one place the web turns an API time (UTC) into what the user sees: the company time zone (timeZone of
/// GET /api/bc/localization) in the user's language format. Never the server or browser time zone
/// (localization.md "시간대", BC-BR-318).
/// </summary>
/// <param name="readTimeZone">Reads the company time zone (an IANA name).</param>
public sealed class CompanyTime(Func<CancellationToken, Task<string>> readTimeZone, IMemoryCache cache)
{
    private const string CacheKey = "quro:company-time-zone";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    /// <summary>The company time zone, cached for every user (there is one company). UTC while the API cannot answer.</summary>
    public async Task<TimeZoneInfo> GetZoneAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out TimeZoneInfo? zone) && zone is not null)
        {
            return zone;
        }
        try
        {
            zone = Find(await readTimeZone(cancellationToken));
        }
        catch (Exception e) when (e is HttpRequestException or ApiProblemException or System.Text.Json.JsonException)
        {
            return TimeZoneInfo.Utc;
        }
        cache.Set(CacheKey, zone, CacheFor);
        return zone;
    }

    /// <summary>Forgets the cached time zone, after the company screen saved a new one.</summary>
    public void Reset() => cache.Remove(CacheKey);

    /// <summary>The time in the zone, short date and time of the current culture (ko 2026-10-30 오전 9:00, en 10/30/2026 9:00 AM).</summary>
    public static string Format(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// IANA time zones the system knows, for the company time zone dropdown (BC-BR-318): code = IANA name,
    /// name = "(UTC+09:00) Asia/Seoul", ordered by standard offset and name.
    /// </summary>
    public static IReadOnlyList<CodeItemDto> TimeZoneOptions => Options.Value;

    private static readonly Lazy<IReadOnlyList<CodeItemDto>> Options = new(() =>
        [.. TimeZoneInfo.GetSystemTimeZones()
            .Select(z => z.HasIanaId ? z.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out var iana) ? iana : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Offset: TimeZoneInfo.FindSystemTimeZoneById(id).BaseUtcOffset))
            .OrderBy(z => z.Offset).ThenBy(z => z.Id, StringComparer.Ordinal)
            .Select(z => new CodeItemDto(z.Id, $"(UTC{(z.Offset < TimeSpan.Zero ? "-" : "+")}{z.Offset:hh\\:mm}) {z.Id}"))]);

    /// <summary>The zone of an IANA name; UTC for a name the system does not know.</summary>
    public static TimeZoneInfo Find(string? timeZone) =>
        timeZone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The host's instance: the time zone comes from the localization API with the user's token.</summary>
    public static CompanyTime Create(IServiceProvider services) =>
        new(async cancellationToken => (await services.GetRequiredService<LocalizationApiClient>()
                .GetAsync(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken)).TimeZone,
            services.GetRequiredService<IMemoryCache>());
}
