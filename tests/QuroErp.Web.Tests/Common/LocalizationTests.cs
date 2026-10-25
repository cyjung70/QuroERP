using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Localization;
using QuroErp.Web.Common;

namespace QuroErp.Web.Tests.Common;

/// <summary>언어 결정·전환 (TASK-CORE-004 "ko/en 전환 시 라벨이 바뀐다", i18n.md).</summary>
public class LocalizationTests(WebFactory factory) : IClassFixture<WebFactory>
{
    private static readonly string EnCookie =
        $"{CookieRequestCultureProvider.DefaultCookieName}={Uri.EscapeDataString(CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("en")))}";

    [Fact, Trait("Id", "CORE-004-WT-01")]
    public async Task LoginPage_NoCookieNoBrowserLanguage_ShowsKo()
    {
        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync("/bc/login"));

        html.Should().Contain("사용자 ID").And.NotContain("User ID").And.Contain("""<html lang="ko">""");
    }

    [Fact, Trait("Id", "CORE-004-WT-02")]
    public async Task LoginPage_BrowserLanguageEn_ShowsEn()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bc/login");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().SendAsync(request)).Content.ReadAsStringAsync());

        html.Should().Contain("User ID").And.NotContain("사용자 ID").And.Contain("""<html lang="en">""");
    }

    [Fact, Trait("Id", "CORE-004-WT-03")]
    public async Task LoginPage_CookieEnOverBrowserKo_ShowsEn()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bc/login");
        request.Headers.AcceptLanguage.ParseAdd("ko-KR");
        request.Headers.Add("Cookie", EnCookie);

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().SendAsync(request)).Content.ReadAsStringAsync());

        html.Should().Contain("User ID");
    }

    [Fact, Trait("Id", "CORE-004-WT-04")]
    public async Task PostCulture_En_SetsCookieRedirectsBackAndLabelsChange()
    {
        var browser = factory.CreateBrowser();
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync("/bc/login"));

        var response = await browser.PostAsync(WebLocalization.CulturePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "en",
            ["returnUrl"] = "/bc/login",
            ["__RequestVerificationToken"] = token,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/bc/login");
        (await browser.GetStringAsync("/bc/login")).Should().Contain("User ID").And.Contain("Sign in");
    }

    [Theory, Trait("Id", "CORE-004-WT-05")]
    [InlineData("ja", "https://evil.example/")]
    [InlineData("en", "//evil.example/")]
    public async Task PostCulture_UnsupportedCultureOrExternalUrl_IgnoresAndRedirectsToRoot(string culture, string returnUrl)
    {
        var browser = factory.CreateBrowser();
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync("/bc/login"));

        var response = await browser.PostAsync(WebLocalization.CulturePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = culture,
            ["returnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = token,
        }));

        response.Headers.Location!.OriginalString.Should().Be("/");
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        cookies.Any(c => c.StartsWith(CookieRequestCultureProvider.DefaultCookieName, StringComparison.Ordinal))
            .Should().Be(culture == "en");
    }

    [Theory, Trait("Id", "CORE-004-WT-06")]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task PostCulture_NoAntiforgeryToken_IsRejected(string environment)
    {
        var response = await factory.CreateBrowser(environment).PostAsync(WebLocalization.CulturePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "en",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
