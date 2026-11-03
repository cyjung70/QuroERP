namespace QuroErp.Web.Auth;

/// <summary>Explicit HTTP-only development hosting; ordinary environments keep HTTPS defaults.</summary>
public static class WebTransport
{
    public const string HttpDevelopmentEnvironment = "DevelopmentHttp";

    public static bool AllowsHttp(IHostEnvironment environment) =>
        environment.IsEnvironment(HttpDevelopmentEnvironment);

    public static bool RequiresSecureCookie(HttpContext context) =>
        !AllowsHttp(context.RequestServices.GetRequiredService<IHostEnvironment>()) || context.Request.IsHttps;
}
