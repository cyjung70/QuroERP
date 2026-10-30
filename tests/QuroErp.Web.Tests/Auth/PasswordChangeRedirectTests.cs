using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests.Auth;

/// <summary>A user who must change the password is kept on the password change screen (BC-BR-009, login.md "로그인 처리" 9).</summary>
public class PasswordChangeRedirectTests(WebFactory factory) : IClassFixture<WebFactory>
{
    [Fact, Trait("Id", "BC-004-WT-02")]
    public async Task Login_MustChangePassword_RedirectsToChangePassword_InsteadOfReturnUrl()
    {
        var (_, response) = await SignInAsync(mustChange: true, returnUrl: "/bc/item");

        response.Headers.Location!.AbsolutePath.Should().Be(WebAuthentication.ChangePasswordPath);
    }

    [Theory, Trait("Id", "BC-004-WT-03")]
    [InlineData("/")]
    [InlineData("/bc/item")]
    public async Task MustChangePassword_OtherPages_RedirectToChangePassword(string path)
    {
        var (browser, _) = await SignInAsync(mustChange: true);

        var response = await browser.SendAsync(Page(path));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(WebAuthentication.ChangePasswordPath);
    }

    [Fact, Trait("Id", "BC-004-WT-04")]
    public async Task MustChangePassword_ChangePasswordPage_Opens()
    {
        var (browser, _) = await SignInAsync(mustChange: true);

        var response = await browser.SendAsync(Page(WebAuthentication.ChangePasswordPath));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Please change your password to continue.");
    }

    [Fact, Trait("Id", "BC-004-WT-05")]
    public async Task NoChangeNeeded_PagesAreNotRedirected()
    {
        var (browser, response) = await SignInAsync(mustChange: false, returnUrl: "/bc/item");

        response.Headers.Location!.AbsolutePath.Should().Be("/bc/item");
        (await browser.SendAsync(Page("/"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage Page(string path) => new(HttpMethod.Get, path) { Headers = { { "Accept", "text/html" } } };

    /// <summary>Signs in through the login page with a fake login response; the page language is en.</summary>
    private async Task<(HttpClient Browser, HttpResponseMessage Response)> SignInAsync(bool mustChange, string? returnUrl = null)
    {
        factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse("token", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", mustChange, "en")),
        };
        // The session cookie is Secure, so the client must use https to send it back
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
        var loginPath = WebAuthentication.LoginPath + (returnUrl is null ? "" : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync(loginPath));
        var response = await browser.PostAsync(loginPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "pass",
            ["__RequestVerificationToken"] = token,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return (browser, response);
    }
}
