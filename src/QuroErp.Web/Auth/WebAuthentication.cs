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
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, Name, Role));
    }
}

/// <summary>Web 호스트 인증 구성 (security.md "인증 경로": 사용자는 쿠키 세션).</summary>
public static class WebAuthentication
{
    public const string LoginPath = "/bc/login";
    public const string LogoutPath = "/bc/logout";

    /// <summary>Api 주소 설정 키.</summary>
    public const string ApiBaseUrlKey = "QuroApi:BaseUrl";

    public static IServiceCollection AddWebAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var apiBaseUrl = configuration[ApiBaseUrlKey]
            ?? throw new InvalidOperationException($"설정 {ApiBaseUrlKey}가 없다. appsettings나 환경변수 QuroApi__BaseUrl로 설정한다.");

        services.AddSingleton<AccessTokenStore>();
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

    /// <summary>로그아웃: 서버의 토큰을 지우고 쿠키를 지운다.</summary>
    public static IEndpointRouteBuilder MapLogout(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LogoutPath, async (HttpContext context, AccessTokenStore store) =>
            {
                if (!AntiforgeryCheck.IsValid(context))
                {
                    return AntiforgeryCheck.Rejected();
                }
                if (context.User.FindFirst(WebAuthClaims.SessionId)?.Value is { } sessionId)
                {
                    store.Remove(sessionId);
                }
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect(LoginPath);
            })
            .WithMetadata(new RequireAntiforgeryTokenAttribute());
        return endpoints;
    }

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
