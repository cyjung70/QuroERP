using System.Net;
using FluentAssertions;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests.Auth;

/// <summary>로그아웃 위조 요청 방지 (security.md).</summary>
public class LogoutTests(WebFactory factory) : IClassFixture<WebFactory>
{
    [Theory, Trait("Id", "CORE-003-WT-01")]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task PostLogout_NoAntiforgeryToken_IsRejected(string environment)
    {
        var response = await factory.CreateBrowser(environment).PostAsync(WebAuthentication.LogoutPath, new FormUrlEncodedContent([]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, Trait("Id", "CORE-003-WT-02")]
    public async Task PostLogout_WithAntiforgeryToken_RedirectsToLogin()
    {
        var browser = factory.CreateBrowser();
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync(WebAuthentication.LoginPath));

        var response = await browser.PostAsync(WebAuthentication.LogoutPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(WebAuthentication.LoginPath);
    }
}
