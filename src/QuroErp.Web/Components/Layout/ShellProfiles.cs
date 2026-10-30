using Microsoft.Extensions.Caching.Memory;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;
using QuroErp.Web.Bc.Localization;
using QuroErp.Web.Common;

namespace QuroErp.Web.Components.Layout;

/// <summary>What the app shell shows for the signed-in user: /me (menus, company, role) and the languages to pick from.</summary>
public sealed record ShellProfile(MeResponse Me, IReadOnlyList<string> Languages);

/// <summary>
/// Reads the shell profile once per page load and keeps it for the session (app-shell.md 3 "데이터"). A full load or a browser
/// reload reads /me again; enhanced navigation between pages reuses it, so menu changes show from the next sign-in or reload.
/// </summary>
public sealed class ShellProfileLoader(
    AccessTokenStore store, AccessTokenProvider tokens, AuthApiClient authApi, LocalizationApiClient localization, IMemoryCache cache)
{
    /// <summary>How long an unused profile stays cached; the idle session ends sooner by default.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    // Blazor enhanced navigation fetches the next page with this in the Accept header; a full load or reload does not
    private const string EnhancedNavigation = "blazor-enhanced-nav";

    /// <summary>
    /// False when the page goes without the shell: no session, or a forced password change (login.md, app-shell.md "셸 밖").
    /// </summary>
    public bool ShowsShell(HttpContext context) =>
        SessionId(context) is { } sessionId && store.Get(sessionId) is { MustChangePassword: false };

    /// <returns>The profile, or null when the Api cannot be reached; the shell then shows no menu and no company.</returns>
    public async Task<ShellProfile?> LoadAsync(HttpContext context, string uiLanguage, CancellationToken cancellationToken)
    {
        if (SessionId(context) is not { } sessionId)
        {
            return null;
        }
        var key = $"shell:{sessionId}";
        var enhanced = context.Request.Headers.Accept.Any(a => a?.Contains(EnhancedNavigation, StringComparison.OrdinalIgnoreCase) == true);
        if (enhanced && cache.TryGetValue(key, out ShellProfile? cached) && cached is not null)
        {
            return cached;
        }
        try
        {
            if (await tokens.GetAsync(cancellationToken) is not { } token)
            {
                return null;
            }
            var me = await authApi.GetMeAsync(token, cancellationToken);
            // JSON leaves a missing list null; the shell then shows an empty menu instead of failing the page
            me = me with { Menus = me.Menus ?? [] };
            var inUse = (await localization.GetAsync(uiLanguage, cancellationToken)).Languages
                .OrderBy(l => l.SortOrder).Select(l => l.LangCode).ToList();
            // Only languages the screens have resources for can be picked (WebLocalization ignores the rest)
            var languages = inUse.Where(WebLocalization.Cultures.Contains).ToList();
            var profile = new ShellProfile(me, languages.Count > 0 ? languages : WebLocalization.Cultures);
            cache.Set(key, profile, new MemoryCacheEntryOptions { SlidingExpiration = CacheFor });
            return profile;
        }
        catch (Exception e) when (e is HttpRequestException or ApiProblemException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? SessionId(HttpContext context) => context.User.FindFirst(WebAuthClaims.SessionId)?.Value;
}
