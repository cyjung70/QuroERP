using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests.Bc;

/// <summary>Login screen /bc/login (BC-UC-001 screen items 1-11, TASK-BC-002) rendered by the real Web host.</summary>
public class LoginScreenTests(WebFactory factory) : IClassFixture<WebFactory>
{
    // Tab order 2 → 3 → 4 → 6 → 7 → 8 → 10 → 1 (spec "화면 항목")
    private static readonly string[] TabOrder =
    [
        "login-header-user-id", "login-header-password", "login-header-show-password", "login-header-remember-id",
        "login-header-keep-signed-in", "login-toolbar-login", "login-forgot-password", "login-language",
    ];

    // Split layout (TASK-CORE-036): the form, then the language selector, then version and copyright
    private static readonly string[] FormPanelOrder = ["login-header-user-id", "login-toolbar-login", "login-language", "login-version"];

    [Fact, Trait("Id", "BC-002-WT-01")]
    public async Task Page_ShowsItemsInTabOrder_WithLimitsAndAutocomplete()
    {
        factory.LoginOptions = new LoginOptionsResponse(true, "ADMIN_ONLY", 30);

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync(WebAuthentication.LoginPath));

        TabOrder.Select(id => html.IndexOf($"data-testid=\"{id}\"", StringComparison.Ordinal))
            .Should().OnlyContain(i => i >= 0).And.BeInAscendingOrder();
        html.Should().Contain("maxlength=\"50\"").And.Contain("maxlength=\"128\"")
            .And.Contain("autocomplete=\"current-password\"").And.Contain("aria-label=\"비밀번호 보기\"")
            .And.Contain("관리자에게 문의하세요").And.Contain("LGPL-3.0")
            .And.NotContain("지우기");
    }

    [Fact, Trait("Id", "BC-UC-001-T13")]
    public async Task Page_AutoLoginOff_HasNoKeepSignedInCheckbox()
    {
        factory.LoginOptions = new LoginOptionsResponse(false, "ADMIN_ONLY", 30);

        var html = await factory.CreateClient().GetStringAsync(WebAuthentication.LoginPath);

        html.Should().NotContain("login-header-keep-signed-in").And.Contain("login-header-remember-id");
    }

    [Fact, Trait("Id", "CORE-036-WT-01")]
    public async Task Page_IsSplit_BrandPanelThenFormPanel_WithLanguageAndVersionInTheFormPanel()
    {
        factory.LoginOptions = new LoginOptionsResponse(false, "ADMIN_ONLY", 30);

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync(WebAuthentication.LoginPath));

        var brand = html.IndexOf("data-testid=\"login-brand\"", StringComparison.Ordinal);
        var panel = html.IndexOf("data-testid=\"login-panel\"", StringComparison.Ordinal);
        brand.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(panel);
        html[brand..panel].Should().Contain("img/quro-logo-white").And.Contain("alt=\"QuroERP\"")
            .And.Contain("오픈소스 ERP 플랫폼").And.Contain("Community Edition").And.NotContain("<input");
        FormPanelOrder.Select(id => html.IndexOf($"data-testid=\"{id}\"", StringComparison.Ordinal))
            .Should().OnlyContain(i => i > panel).And.BeInAscendingOrder();
        html.Should().Contain("img/quro-logo").And.NotContain("login-scene");
    }

    [Fact, Trait("Id", "BC-002-WT-02")]
    public async Task Page_AutoLoginOn_ShowsTheCheckboxWithAHiddenSharedComputerWarning()
    {
        factory.LoginOptions = new LoginOptionsResponse(true, "ADMIN_ONLY", 30);

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync(WebAuthentication.LoginPath));

        html.Should().Contain("data-testid=\"login-header-keep-signed-in\"");
        html.Should().MatchRegex("data-testid=\"login-keep-signed-in-warning\"[^>]*hidden[^>]*>공용 PC에서는");
    }

    [Fact, Trait("Id", "BC-UC-001-T01")]
    public async Task Submit_EmptyIdAndPassword_ShowsAuth001BelowBothFieldsWithoutCallingLogin()
    {
        factory.LoginOptions = null;
        var (browser, token) = await OpenAsync();
        factory.AuthRequests.Clear();

        var html = WebUtility.HtmlDecode(await (await PostAsync(browser, token, "", "")).Content.ReadAsStringAsync());

        html.Should().MatchRegex("id=\"user-id-error\"[^>]*>사용자 ID을\\(를\\) 입력하세요.<")
            .And.MatchRegex("id=\"password-error\"[^>]*>비밀번호을\\(를\\) 입력하세요.<");
        factory.AuthRequests.Should().NotContain(p => p.EndsWith(AuthRoutes.Login, StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "BC-UC-001-T03")]
    public async Task Submit_WrongPassword_ShowsAuth002AboveTheForm()
    {
        factory.LoginOptions = null;
        factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"status":401,"code":"AUTH-002"}""", null, "application/problem+json"),
        };
        var (browser, token) = await OpenAsync();

        var html = WebUtility.HtmlDecode(await (await PostAsync(browser, token, "kim", "wrong")).Content.ReadAsStringAsync());

        html.Should().MatchRegex("role=\"alert\" data-testid=\"login-message\"[^>]*>아이디 또는 비밀번호가 올바르지 않습니다.<");
    }

    [Theory, Trait("Id", "BC-UC-001-T22")]
    [InlineData("/bc/item", "/bc/item")]
    [InlineData("https://evil.example/", "/")]
    public async Task Submit_Success_FollowsOnlyLocalReturnUrls(string returnUrl, string expected)
    {
        SignInResponse();
        var (browser, token) = await OpenAsync(returnUrl);

        using var response = await PostAsync(browser, token, "kim", "pass", returnUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.Host.Should().Be("localhost");
        response.Headers.Location!.AbsolutePath.Should().Be(expected);
    }

    [Fact, Trait("Id", "BC-002-WT-03")]
    public async Task MainScreen_AfterSignIn_ShowsThePreviousSignInTimeAndIp()
    {
        SignInResponse();
        var (browser, token) = await OpenAsync();
        await PostAsync(browser, token, "kim", "pass");

        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/"));

        html.Should().MatchRegex("data-testid=\"home-last-login\"[^>]*>직전 로그인: [^<]+\\(IP 10\\.0\\.0\\.7\\)<");
    }

    private void SignInResponse()
    {
        factory.LoginOptions = null;
        factory.ApiResponse = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new LoginResponse("token", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", false, "ko",
                new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero), "10.0.0.7")),
        };
    }

    // The session cookie is Secure, so the client uses https to send it back
    private async Task<(HttpClient Browser, string Token)> OpenAsync(string? returnUrl = null)
    {
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync(LoginPath(returnUrl)));
        return (browser, token);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient browser, string token, string userId, string password, string? returnUrl = null) =>
        browser.PostAsync(LoginPath(returnUrl), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = userId,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));

    private static string LoginPath(string? returnUrl) =>
        WebAuthentication.LoginPath + (returnUrl is null ? "" : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
}
