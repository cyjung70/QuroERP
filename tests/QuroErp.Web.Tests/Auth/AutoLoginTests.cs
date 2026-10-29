using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests.Auth;

/// <summary>"Keep me signed in" on the Web host (login.md "자동 로그인", "로그아웃"; TASK-BC-002 part 2).</summary>
public class AutoLoginTests(WebFactory factory) : IClassFixture<WebFactory>
{
    private const string TokenCookie = AutoLoginCookie.Name + "=";

    [Fact, Trait("Id", "BC-002-WT-04")]
    public async Task Login_KeepSignedIn_SetsAStrictHttpOnlyTokenCookie()
    {
        Reset();
        factory.LoginOptions = new LoginOptionsResponse(true, "ADMIN_ONLY", 30);
        factory.ApiResponse = () => Ok(Login("raw-1"));
        var browser = Browser(handleCookies: true);

        using var response = await PostLoginAsync(browser, keepSignedIn: true);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.AuthCalls.Should().Contain(c => c.Path == "/" + AuthRoutes.Login && c.Body.Contains("\"autoLogin\":true"));
        var cookie = TokenCookies(response).Should().ContainSingle().Subject.ToLowerInvariant();
        cookie.Should().StartWith(TokenCookie.ToLowerInvariant() + "raw-1;")
            .And.Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("expires=");
    }

    [Fact, Trait("Id", "BC-002-WT-05")]
    public async Task Login_WithoutKeepSignedIn_DeletesAnOldTokenCookie()
    {
        Reset();
        factory.ApiResponse = () => Ok(Login(null));
        var browser = Browser(handleCookies: true);

        using var response = await PostLoginAsync(browser, keepSignedIn: false);

        TokenCookies(response).Should().ContainSingle().Which.Should().StartWith(TokenCookie + ";").And.Contain("expires=Thu, 01 Jan 1970");
    }

    [Fact, Trait("Id", "BC-UC-001-T14")]
    public async Task Page_NoSession_WithTokenCookie_SignsInWithoutTheLoginScreenAndReplacesTheToken()
    {
        Reset();
        factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.AutoLogin, StringComparison.Ordinal) ? Ok(Login("raw-2")) : null;

        using var response = await Browser().SendAsync(PageRequest("/", "raw-1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Should().Contain("data-testid=\"home-last-login\"");
        factory.AuthCalls.Should().ContainSingle(c => c.Path == "/" + AuthRoutes.AutoLogin).Which.Body.Should().Contain("\"token\":\"raw-1\"");
        TokenCookies(response).Should().ContainSingle().Which.Should().StartWith(TokenCookie + "raw-2;");
        response.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("QuroErp.Session=", StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "BC-UC-001-T15")]
    public async Task Page_TokenRejected_DeletesTheCookieAndShowsAuth007OnTheLoginScreen()
    {
        Reset();
        factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.AutoLogin, StringComparison.Ordinal)
            ? Problem(HttpStatusCode.Unauthorized, "AUTH-007")
            : null;
        var browser = Browser();

        using var response = await browser.SendAsync(PageRequest("/bc/item?id=3", "revoked"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/bc/login?reason=AUTH-007&ReturnUrl=%2Fbc%2Fitem%3Fid%3D3");
        TokenCookies(response).Should().ContainSingle().Which.Should().Contain("expires=Thu, 01 Jan 1970");

        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(response.Headers.Location));
        html.Should().MatchRegex("data-testid=\"login-message\"[^>]*>자동 로그인이 만료되었습니다. 다시 로그인하세요.<");
    }

    [Fact, Trait("Id", "BC-002-WT-06")]
    public async Task Page_AutoLoginRateLimited_KeepsTheCookieAndContinuesSignedOut()
    {
        Reset();
        factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.AutoLogin, StringComparison.Ordinal)
            ? Problem(HttpStatusCode.TooManyRequests, "AUTH-008")
            : null;

        using var response = await Browser().SendAsync(PageRequest(WebAuthentication.LoginPath, "raw-limited"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).Should().NotContain(c => c.StartsWith(TokenCookie, StringComparison.Ordinal));
    }

    [Fact, Trait("Id", "BC-002-WT-07")]
    public async Task Page_QueryReason_ShowsOnlyKnownCodes()
    {
        Reset();

        var html = WebUtility.HtmlDecode(await Browser().GetStringAsync(WebAuthentication.LoginPath + "?reason=AUTH-002"));

        html.Should().MatchRegex("data-testid=\"login-message\"[^>]*hidden[^>]*></p>");
    }

    [Fact, Trait("Id", "BC-002-WT-08")]
    public async Task Logout_RevokesThisDevicesTokenOnTheApiAndDeletesTheCookie()
    {
        Reset();
        factory.LoginOptions = new LoginOptionsResponse(true, "ADMIN_ONLY", 30);
        factory.ApiResponse = () => Ok(Login("raw-1"));
        factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.Logout, StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : null;
        var browser = Browser(handleCookies: true);
        await PostLoginAsync(browser, keepSignedIn: true);
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync("/"));

        using var response = await browser.PostAsync(WebAuthentication.LogoutPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));

        response.Headers.Location!.OriginalString.Should().Be(WebAuthentication.LoginPath);
        var logout = factory.AuthCalls.Should().ContainSingle(c => c.Path == "/" + AuthRoutes.Logout).Subject;
        logout.Bearer.Should().Be("access");
        logout.Body.Should().Contain("\"autoLoginToken\":\"raw-1\"");
        TokenCookies(response).Should().ContainSingle().Which.Should().Contain("expires=Thu, 01 Jan 1970");
    }

    [Fact, Trait("Id", "BC-002-WT-09")]
    public async Task Logout_AfterTheSessionExpired_RevokesTheTokenWithoutABearerAndDeletesTheCookie()
    {
        Reset();
        factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.Logout, StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : null;
        // The page's antiforgery token was issued to the signed-in user and no longer validates
        using var request = new HttpRequestMessage(HttpMethod.Post, WebAuthentication.LogoutPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = "stale" }),
        };
        request.Headers.Add("Cookie", TokenCookie + "raw-expired");

        using var response = await Browser().SendAsync(request);

        response.Headers.Location!.OriginalString.Should().Be(WebAuthentication.LoginPath);
        var logout = factory.AuthCalls.Should().ContainSingle(c => c.Path == "/" + AuthRoutes.Logout).Subject;
        logout.Bearer.Should().BeNull();
        logout.Body.Should().Contain("\"autoLoginToken\":\"raw-expired\"");
        TokenCookies(response).Should().ContainSingle().Which.Should().Contain("expires=Thu, 01 Jan 1970");
    }

    [Fact, Trait("Id", "BC-002-WT-10")]
    public async Task Logout_SignedOutWithoutTheTokenCookie_StillNeedsTheAntiforgeryToken()
    {
        Reset();

        using var response = await Browser().PostAsync(WebAuthentication.LogoutPath, new FormUrlEncodedContent([]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.AuthCalls.Should().BeEmpty();
    }

    [Theory, Trait("Id", "BC-UC-001-T22")]
    [InlineData("/bc/item", "/bc/item")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    public async Task AutoLogin_OnTheLoginPage_FollowsOnlyLocalReturnUrls(string returnUrl, string expected)
    {
        Reset();
        var token = "raw-return-" + Guid.NewGuid().ToString("N");
        factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.AutoLogin, StringComparison.Ordinal) ? Ok(Login("raw-next")) : null;

        using var response = await Browser().SendAsync(
            PageRequest(WebAuthentication.LoginPath + "?ReturnUrl=" + Uri.EscapeDataString(returnUrl), token));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(expected);
    }

    [Fact, Trait("Id", "BC-UC-001-T22")]
    public async Task AutoLoginFailure_OnAProtocolRelativePath_ReturnsToTheMainScreenAfterSignIn()
    {
        Reset();
        factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.AutoLogin, StringComparison.Ordinal)
            ? Problem(HttpStatusCode.Unauthorized, "AUTH-007")
            : null;
        var browser = Browser(handleCookies: true);

        // An absolute URI keeps the "//evil.example" path on this site
        using var failed = await Browser().SendAsync(PageRequest("https://localhost//evil.example/x", "raw-evil"));
        var loginPage = failed.Headers.Location!.OriginalString;
        loginPage.Should().Be("/bc/login?reason=AUTH-007&ReturnUrl=%2F%2Fevil.example%2Fx");

        factory.ApiResponse = () => Ok(Login(null));
        var antiforgery = WebFactory.AntiforgeryToken(await browser.GetStringAsync(loginPage));
        using var signedIn = await browser.PostAsync(loginPage, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "pass",
            ["__RequestVerificationToken"] = antiforgery,
        }));

        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.Host.Should().Be("localhost");
        signedIn.Headers.Location!.AbsolutePath.Should().Be("/");
    }

    [Fact, Trait("Id", "BC-002-UT-01")]
    public async Task Coordinator_SameTokenTogether_CallsTheApiOnce()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));
        var coordinator = new AutoLoginCoordinator(clock);
        var calls = 0;
        var answer = new TaskCompletionSource<LoginResponse>();
        Task<LoginResponse> Call()
        {
            Interlocked.Increment(ref calls);
            return answer.Task;
        }

        var first = coordinator.SignInAsync("raw-1", Call);
        var second = coordinator.SignInAsync("raw-1", Call);
        answer.SetResult(Login("raw-2"));
        (await first).Should().BeSameAs(await second);
        calls.Should().Be(1);

        clock.Advance(AutoLoginCoordinator.ReuseFor + TimeSpan.FromSeconds(1));
        await coordinator.SignInAsync("raw-1", Call);
        calls.Should().Be(2);
    }

    private void Reset()
    {
        factory.LoginOptions = null;
        factory.ApiResponse = () => Problem(HttpStatusCode.Unauthorized, "AUTH-002");
        factory.AuthRouteResponse = _ => null;
        factory.AuthCalls.Clear();
    }

    private static LoginResponse Login(string? autoLoginToken) =>
        new("access", DateTimeOffset.UtcNow.AddHours(24), "kim", "Kim", "user", false, "ko",
            new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero), "10.0.0.7",
            autoLoginToken, autoLoginToken is null ? null : DateTimeOffset.UtcNow.AddDays(14));

    private static HttpResponseMessage Ok(LoginResponse login) => new(HttpStatusCode.OK) { Content = JsonContent.Create(login) };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent($$"""{"status":{{(int)status}},"code":"{{code}}"}""", null, "application/problem+json"),
    };

    // The session and token cookies are Secure, so the client uses https to send them back
    private HttpClient Browser(bool handleCookies = false) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false, HandleCookies = handleCookies, BaseAddress = new Uri("https://localhost"),
    });

    // A browser page request carrying only the token cookie (no session)
    private static HttpRequestMessage PageRequest(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Cookie", TokenCookie + token);
        return request;
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient browser, bool keepSignedIn)
    {
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync(WebAuthentication.LoginPath));
        var form = new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "pass",
            ["__RequestVerificationToken"] = token,
        };
        if (keepSignedIn)
        {
            form["Input.KeepSignedIn"] = "true";
        }
        return await browser.PostAsync(WebAuthentication.LoginPath, new FormUrlEncodedContent(form));
    }

    private static IEnumerable<string> TokenCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Where(c => c.StartsWith(TokenCookie, StringComparison.Ordinal))
            : [];
}
