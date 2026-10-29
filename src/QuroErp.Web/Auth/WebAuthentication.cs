using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Auth;

/// <summary>쿠키 세션 클레임. 쿠키에는 세션 ID와 표시용 정보만 넣는다 (토큰은 넣지 않는다).</summary>
public static class WebAuthClaims
{
    public const string SessionId = "sid";
    public const string Name = "name";
    public const string Role = "role";

    /// <summary>Previous sign-in time (ISO 8601) and IP, shown on the main screen (login.md "로그인 처리" 11).</summary>
    public const string LastLoginAt = "last_login_at";
    public const string LastLoginIp = "last_login_ip";

    public static ClaimsPrincipal CreatePrincipal(string sessionId, LoginResponse login)
    {
        var claims = new List<Claim>
        {
            new(SessionId, sessionId),
            new(Name, login.UserName ?? login.UserId),
        };
        if (login.RoleCode is not null)
        {
            claims.Add(new Claim(Role, login.RoleCode));
        }
        if (login.LastLoginAt is { } lastLoginAt)
        {
            claims.Add(new Claim(LastLoginAt, lastLoginAt.ToString("O", CultureInfo.InvariantCulture)));
        }
        if (login.LastLoginIp is not null)
        {
            claims.Add(new Claim(LastLoginIp, login.LastLoginIp));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, Name, Role));
    }
}

/// <summary>Web 호스트 인증 구성 (security.md "인증 경로": 사용자는 쿠키 세션).</summary>
public static class WebAuthentication
{
    public const string LoginPath = "/bc/login";
    public const string LogoutPath = "/bc/logout";
    public const string ChangePasswordPath = "/bc/change-password";

    /// <summary>Query parameter of the login page with the message code to show above the form (AUTH-006, AUTH-007).</summary>
    public const string ReasonQuery = "reason";
    public const string AutoLoginExpiredCode = "AUTH-007";

    // Pages a user who must change the password may still open (login.md "로그인 처리" 9)
    private static readonly string[] PasswordChangeAllowedPaths = [ChangePasswordPath, LoginPath, LogoutPath, "/not-found", "/Error"];

    /// <summary>Api 주소 설정 키.</summary>
    public const string ApiBaseUrlKey = "QuroApi:BaseUrl";

    public static IServiceCollection AddWebAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var apiBaseUrl = configuration[ApiBaseUrlKey]
            ?? throw new InvalidOperationException($"설정 {ApiBaseUrlKey}가 없다. appsettings나 환경변수 QuroApi__BaseUrl로 설정한다.");

        services.AddSingleton<AccessTokenStore>();
        services.AddSingleton<AutoLoginCoordinator>();
        services.AddScoped<AccessTokenProvider>();
        services.AddHttpClient<AuthApiClient>(http => http.BaseAddress = new Uri(apiBaseUrl));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = LoginPath;
                o.Cookie.Name = "QuroErp.Session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = TimeSpan.FromHours(8); // [추정] 사용하지 않으면 8시간 뒤 세션 종료
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = RejectUnknownSession;
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        return services;
    }

    /// <summary>Login page showing a message code above the form, keeping a local return address.</summary>
    public static string LoginPathWith(string reason, string? returnUrl = null) =>
        $"{LoginPath}?{ReasonQuery}={Uri.EscapeDataString(reason)}"
        + (string.IsNullOrEmpty(returnUrl) ? "" : "&ReturnUrl=" + Uri.EscapeDataString(returnUrl));

    /// <summary>
    /// 로그아웃: Api가 이 기기의 자동 로그인 토큰을 폐기하고 LOGOUT 이력을 남긴다 (login.md "로그아웃").
    /// Then the server-side token, the session cookie and the token cookie are removed even if the Api call failed.
    /// The remembered user ID stays (it is in the browser's localStorage).
    /// </summary>
    public static IEndpointRouteBuilder MapLogout(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LogoutPath, async (HttpContext context, AccessTokenStore store, AuthApiClient authApi) =>
            {
                var sessionId = context.User.FindFirst(WebAuthClaims.SessionId)?.Value;
                var token = AutoLoginCookie.Read(context.Request);
                // After the session expired, the page's antiforgery token was issued to the signed-in user and no longer
                // validates. A signed-out request carrying the token cookie is same-site (the cookie is SameSite=Strict),
                // so it may still revoke that token.
                if (!AntiforgeryCheck.IsValid(context) && !(sessionId is null && token is not null))
                {
                    return AntiforgeryCheck.Rejected();
                }
                var login = sessionId is null ? null : store.Get(sessionId);
                if (login is not null || token is not null)
                {
                    try
                    {
                        // Without a session the Api revokes the token alone (login.md "로그아웃")
                        await authApi.LogoutAsync(login?.AccessToken, new LogoutRequest(token),
                            context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(),
                            context.RequestAborted);
                    }
                    catch (Exception e) when (e is ApiProblemException or HttpRequestException)
                    {
                        // Signing out of the Web host does not depend on the Api; an unrevoked token still expires
                    }
                }
                if (sessionId is not null)
                {
                    store.Remove(sessionId);
                }
                AutoLoginCookie.Delete(context.Response);
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect(LoginPath);
            })
            .WithMetadata(new RequireAntiforgeryTokenAttribute());
        return endpoints;
    }

    /// <summary>
    /// Sends page requests of a user who must change the password to the password change screen (BC-BR-009).
    /// The API also answers 403 AUTH-005; this keeps the user on the screen that can fix it. Use after UseAuthentication.
    /// </summary>
    public static IApplicationBuilder UsePasswordChangeRedirect(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method)
                && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)
                && !PasswordChangeAllowedPaths.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase)
                && MustChangePassword(context))
            {
                context.Response.Redirect(ChangePasswordPath);
                return;
            }
            await next();
        });

    private static bool MustChangePassword(HttpContext context) =>
        context.User.FindFirst(WebAuthClaims.SessionId)?.Value is { } sessionId
        && context.RequestServices.GetRequiredService<AccessTokenStore>().Get(sessionId) is { MustChangePassword: true };

    // Web을 다시 시작했거나 토큰이 만료돼 서버에 토큰이 없으면 쿠키 세션도 끝낸다
    private static async Task RejectUnknownSession(CookieValidatePrincipalContext context)
    {
        var store = context.HttpContext.RequestServices.GetRequiredService<AccessTokenStore>();
        var sessionId = context.Principal?.FindFirst(WebAuthClaims.SessionId)?.Value;
        if (sessionId is null || store.Get(sessionId) is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
