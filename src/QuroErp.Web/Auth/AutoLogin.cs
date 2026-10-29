using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Auth;

/// <summary>
/// "Keep me signed in" token cookie (login.md "자동 로그인"). The Web host sets and deletes it; the Api checks and replaces the token.
/// Only the raw token is in the cookie; the Api keeps its SHA-256 hash.
/// </summary>
public static class AutoLoginCookie
{
    public const string Name = "QuroErp.AutoLogin";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var token) && !string.IsNullOrEmpty(token) ? token : null;

    /// <summary>Sets the token from a login response, or deletes the cookie when no token was issued.</summary>
    public static void Apply(HttpResponse response, LoginResponse login)
    {
        if (login.AutoLoginToken is null)
        {
            Delete(response);
            return;
        }
        response.Cookies.Append(Name, login.AutoLoginToken, Options(login.AutoLoginExpiresAt));
    }

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}

/// <summary>
/// Shares one auto-login call per token. The Api replaces the token on every use and treats a second use as theft
/// (revokes all the user's tokens), so page requests that arrive together (several tabs, reloads) must not each call it.
/// [추정] A result is reused for <see cref="ReuseFor"/>; a later request has the new cookie by then.
/// In memory, like <see cref="AccessTokenStore"/>: the Web host runs as a single instance (login.md "세션").
/// </summary>
public sealed class AutoLoginCoordinator(TimeProvider clock)
{
    public static readonly TimeSpan ReuseFor = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, Lazy<Task<LoginResponse>> Call)> _calls = new(StringComparer.Ordinal);

    public Task<LoginResponse> SignInAsync(string token, Func<Task<LoginResponse>> call)
    {
        var now = clock.GetUtcNow();
        foreach (var (key, entry) in _calls)
        {
            if (now - entry.At > ReuseFor)
            {
                _calls.TryRemove(key, out _);
            }
        }
        return _calls.GetOrAdd(token, _ => (now, new Lazy<Task<LoginResponse>>(call))).Call.Value;
    }
}

public static class AutoLoginMiddleware
{
    /// <summary>
    /// A page request without a session but with the token cookie signs in with the token (login.md "자동 로그인" 1).
    /// Failure (401 AUTH-007) deletes the cookie and shows the login screen with AUTH-007. Use after UseAuthentication.
    /// </summary>
    public static IApplicationBuilder UseAutoLogin(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method)
                || !context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)
                || context.User.Identity?.IsAuthenticated == true
                || AutoLoginCookie.Read(context.Request) is not { } token)
            {
                await next();
                return;
            }

            var services = context.RequestServices;
            LoginResponse login;
            try
            {
                var authApi = services.GetRequiredService<AuthApiClient>();
                login = await services.GetRequiredService<AutoLoginCoordinator>().SignInAsync(token, () => authApi.AutoLoginAsync(
                    token, context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), CancellationToken.None));
            }
            catch (ApiProblemException e) when (e.Problem.Status == StatusCodes.Status401Unauthorized)
            {
                // The token is expired, revoked or auto login is off: it will never work again
                AutoLoginCookie.Delete(context.Response);
                var returnUrl = IsLoginPage(context) ? null : context.Request.Path + context.Request.QueryString;
                context.Response.Redirect(WebAuthentication.LoginPathWith(WebAuthentication.AutoLoginExpiredCode, returnUrl));
                return;
            }
            catch (Exception e) when (e is ApiProblemException or HttpRequestException)
            {
                // Api unreachable or rate limited (AUTH-008): keep the token for the next request and continue signed out
                await next();
                return;
            }

            var idleTimeout = await IdleTimeoutAsync(services.GetRequiredService<AuthApiClient>(), context.RequestAborted);
            var principal = WebAuthClaims.CreatePrincipal(services.GetRequiredService<AccessTokenStore>().Add(login, idleTimeout), login);
            await context.SignInAsync(principal);
            context.User = principal;
            AutoLoginCookie.Apply(context.Response, login);
            WebLocalization.SetCultureCookie(context, login.Language);
            if (IsLoginPage(context))
            {
                context.Response.Redirect(LocalUrl.OrRoot(context.Request.Query["ReturnUrl"].ToString()));
                return;
            }
            await next();
        });

    // session_idle_minutes from the login options; the default when the Api cannot answer
    private static async Task<TimeSpan?> IdleTimeoutAsync(AuthApiClient authApi, CancellationToken cancellationToken)
    {
        try
        {
            return TimeSpan.FromMinutes((await authApi.GetLoginOptionsAsync(cancellationToken)).SessionIdleMinutes);
        }
        catch (Exception e) when (e is ApiProblemException or HttpRequestException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool IsLoginPage(HttpContext context) =>
        string.Equals(context.Request.Path.Value, WebAuthentication.LoginPath, StringComparison.OrdinalIgnoreCase);
}
