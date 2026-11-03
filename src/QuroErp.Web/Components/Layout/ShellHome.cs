using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Localization;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Components.Layout;

/// <summary>The home's welcome line (app-shell.md 6), shared by the home page and the home tab.</summary>
public static class ShellHome
{
    /// <summary>The previous sign-in time and IP in the company time zone (BC-BR-318); UTC when the zone is unknown.</summary>
    public static string? LastLogin(ClaimsPrincipal user, string? timeZone, IStringLocalizer<SharedResource> localizer) =>
        DateTimeOffset.TryParse(user.FindFirst(WebAuthClaims.LastLoginAt)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? localizer["Common.LastLogin", CompanyTime.Format(at, CompanyTime.Find(timeZone)), user.FindFirst(WebAuthClaims.LastLoginIp)?.Value ?? "-"].Value
            : null;
}
