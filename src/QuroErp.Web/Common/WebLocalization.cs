using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Antiforgery;

namespace QuroErp.Web.Common;

/// <summary>
/// Web 다국어 구성 (i18n.md). 언어는 사용자 설정(언어 쿠키) → 브라우저 언어(Accept-Language) → ko 순으로 정한다.
/// </summary>
public static class WebLocalization
{
    public const string DefaultCulture = "ko";

    /// <summary>언어 바꾸기 (POST, 폼 필드 culture·returnUrl).</summary>
    public const string CulturePath = "/culture";

    public static IReadOnlyList<string> Cultures { get; } = [DefaultCulture, "en"];

    public static IServiceCollection AddWebLocalization(this IServiceCollection services)
    {
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddScoped<ProblemMessages>();
        return services;
    }

    public static IApplicationBuilder UseWebLocalization(this IApplicationBuilder app) =>
        app.UseRequestLocalization(o =>
        {
            o.SetDefaultCulture(DefaultCulture)
                .AddSupportedCultures([.. Cultures])
                .AddSupportedUICultures([.. Cultures]);
            o.RequestCultureProviders =
            [
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

    /// <summary>언어 쿠키를 저장하고 원래 화면으로 돌아간다. 지원하지 않는 언어는 무시한다.</summary>
    public static IEndpointRouteBuilder MapCulture(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(CulturePath, async (HttpContext context) =>
            {
                if (!AntiforgeryCheck.IsValid(context))
                {
                    return AntiforgeryCheck.Rejected();
                }
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var culture = form["culture"].ToString();
                if (Cultures.Contains(culture))
                {
                    context.Response.Cookies.Append(
                        CookieRequestCultureProvider.DefaultCookieName,
                        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                        new CookieOptions
                        {
                            Expires = DateTimeOffset.UtcNow.AddYears(1),
                            IsEssential = true,
                            HttpOnly = true,
                            SameSite = SameSiteMode.Lax,
                            Secure = context.Request.IsHttps,
                        });
                }
                return Results.LocalRedirect(LocalUrl.OrRoot(form["returnUrl"]));
            })
            .AllowAnonymous() // 로그인 전(로그인 화면)에도 언어를 바꾼다
            .WithMetadata(new RequireAntiforgeryTokenAttribute());
        return endpoints;
    }
}
