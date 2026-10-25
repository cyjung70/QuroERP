using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Localization;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Web.Tests.Common;

/// <summary>로그인하면 사용자 언어 설정(sys_app_user.lang_code)을 언어 쿠키로 저장한다 (i18n.md "사용자 설정 → 브라우저 언어 → ko").</summary>
public class UserLanguageTests(WebFactory factory) : IClassFixture<WebFactory>
{
    [Theory, Trait("Id", "CORE-004-WT-12")]
    [InlineData("en", "c%3Den%7Cuic%3Den")]
    [InlineData(null, null)]
    public async Task LoginPost_Success_SetsCultureCookieOnlyWhenUserHasLangCode(string? langCode, string? expectedCookieValue)
    {
        factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse("token", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "admin", false, langCode)),
        };
        var browser = factory.CreateBrowser();
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync("/bc/login"));

        var response = await browser.PostAsync("/bc/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "secret",
            ["__RequestVerificationToken"] = token,
        }));

        var cultureCookie = (response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [])
            .FirstOrDefault(c => c.StartsWith(CookieRequestCultureProvider.DefaultCookieName + "=", StringComparison.Ordinal));
        if (expectedCookieValue is null)
        {
            cultureCookie.Should().BeNull();
        }
        else
        {
            cultureCookie.Should().StartWith($"{CookieRequestCultureProvider.DefaultCookieName}={expectedCookieValue};");
        }
    }
}
