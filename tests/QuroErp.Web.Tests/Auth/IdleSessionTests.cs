using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests.Auth;

/// <summary>Idle session on the Web host (login.md "세션", TASK-BC-002 part 3) with a clock the test moves.</summary>
public sealed class IdleSessionTests : IClassFixture<WebFactory>, IDisposable
{
    private readonly WebFactory _factory;
    private readonly TestClock _clock = new(DateTimeOffset.UtcNow);
    private readonly WebApplicationFactory<Program> _host;

    public IdleSessionTests(WebFactory factory)
    {
        _factory = factory;
        _factory.LoginOptions = null;
        _factory.AuthRouteResponse = _ => null;
        _factory.ApiResponse = () => Ok(Login(null));
        _host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(_clock)));
    }

    public void Dispose() => _host.Dispose();

    [Fact, Trait("Id", "BC-UC-001-T19")]
    public async Task Page_After30MinutesWithoutRequests_GoesToTheLoginScreenWithAuth006()
    {
        var browser = await SignInAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));

        using var response = await browser.SendAsync(Page("/"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.AbsolutePath.Should().Be(WebAuthentication.LoginPath);
        location.Query.Should().Contain("reason=AUTH-006").And.Contain("ReturnUrl=%2F");
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(location));
        html.Should().MatchRegex("data-testid=\"login-message\"[^>]*>오랫동안 사용하지 않아 로그아웃되었습니다. 다시 로그인하세요.<");
    }

    [Fact, Trait("Id", "BC-002-WT-11")]
    public async Task Page_Requests_RestartTheIdleTimeout()
    {
        var browser = await SignInAsync();
        _clock.Advance(TimeSpan.FromMinutes(20));
        (await browser.SendAsync(Page("/"))).StatusCode.Should().Be(HttpStatusCode.OK);
        _clock.Advance(TimeSpan.FromMinutes(20));

        (await StatusAsync(browser))!.RemainingSeconds.Should().Be(600);
    }

    [Fact, Trait("Id", "BC-002-WT-12")]
    public async Task Status_IsNotActivity_AndAnswers401OnceTheSessionEnded()
    {
        var browser = await SignInAsync();
        _clock.Advance(TimeSpan.FromMinutes(20));
        (await StatusAsync(browser)).Should().Be(new SessionStatusResponse(600, 120));
        _clock.Advance(TimeSpan.FromMinutes(10));

        using var response = await browser.GetAsync(WebAuthentication.SessionStatusPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-002-WT-13")]
    public async Task SignIn_UsesSessionIdleMinutesFromTheLoginOptions()
    {
        _factory.LoginOptions = new LoginOptionsResponse(false, "ADMIN_ONLY", 10);

        var browser = await SignInAsync();

        (await StatusAsync(browser))!.RemainingSeconds.Should().Be(600);
    }

    [Fact, Trait("Id", "BC-002-WT-14")]
    public async Task Extend_WithTheAntiforgeryToken_StartsTheIdleTimeoutAgain()
    {
        var browser = await SignInAsync();
        var html = await (await browser.SendAsync(Page("/"))).Content.ReadAsStringAsync();
        html.Should().MatchRegex("data-testid=\"session-warning\" hidden");
        var token = WebFactory.AntiforgeryToken(html);
        _clock.Advance(TimeSpan.FromMinutes(29));

        using var rejected = await browser.PostAsync(WebAuthentication.SessionExtendPath, new FormUrlEncodedContent([]));
        using var extended = await browser.PostAsync(WebAuthentication.SessionExtendPath, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        extended.StatusCode.Should().Be(HttpStatusCode.OK);
        (await extended.Content.ReadFromJsonAsync<SessionStatusResponse>())!.RemainingSeconds.Should().Be(1800);
    }

    [Theory, Trait("Id", "BC-002-WT-15")]
    [InlineData("/bc/item", "/bc/login?reason=AUTH-006&ReturnUrl=%2Fbc%2Fitem")]
    [InlineData("https://evil.example/", "/bc/login?reason=AUTH-006&ReturnUrl=%2F")]
    [InlineData("//evil.example/", "/bc/login?reason=AUTH-006&ReturnUrl=%2F")]
    public async Task Expired_WithoutASession_GoesToTheLoginScreenWithAuth006AndALocalReturnUrl(string returnUrl, string expected)
    {
        using var response = await Browser().SendAsync(Page(Expired(returnUrl)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(expected);
    }

    [Fact, Trait("Id", "BC-002-WT-16")]
    public async Task Expired_SessionStillActive_GoesBackToTheScreen()
    {
        var browser = await SignInAsync();

        using var response = await browser.SendAsync(Page(Expired("/bc/item")));

        response.Headers.Location!.OriginalString.Should().Be("/bc/item");
    }

    [Fact, Trait("Id", "BC-002-WT-17")]
    public async Task Page_IdleEndedWithTheTokenCookie_SignsInAgainInsteadOfAuth006()
    {
        _factory.LoginOptions = new LoginOptionsResponse(true, "ADMIN_ONLY", 30);
        _factory.ApiResponse = () => Ok(Login("raw-idle-" + Guid.NewGuid().ToString("N")));
        var browser = await SignInAsync(keepSignedIn: true);
        _factory.AuthRouteResponse = path => path.EndsWith(AuthRoutes.AutoLogin, StringComparison.Ordinal)
            ? Ok(Login("raw-next-" + Guid.NewGuid().ToString("N")))
            : null;
        _clock.Advance(TimeSpan.FromMinutes(31));

        using var response = await browser.SendAsync(Page("/"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.AuthCalls.Should().Contain(c => c.Path == "/" + AuthRoutes.AutoLogin);
    }

    private static string Expired(string returnUrl) =>
        WebAuthentication.SessionExpiredPath + "?returnUrl=" + Uri.EscapeDataString(returnUrl);

    private LoginResponse Login(string? autoLoginToken) =>
        new("access", _clock.GetUtcNow().AddHours(24), "kim", "Kim", "user", false, "ko", null, null,
            autoLoginToken, autoLoginToken is null ? null : _clock.GetUtcNow().AddDays(14));

    private static HttpResponseMessage Ok(LoginResponse login) => new(HttpStatusCode.OK) { Content = JsonContent.Create(login) };

    // The session cookie is Secure, so the client uses https to send it back
    private HttpClient Browser() => _host.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost"),
    });

    private static HttpRequestMessage Page(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Accept", "text/html");
        return request;
    }

    private async Task<HttpClient> SignInAsync(bool keepSignedIn = false)
    {
        var browser = Browser();
        var form = new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserId"] = "kim",
            ["Input.Password"] = "pass",
            ["__RequestVerificationToken"] = WebFactory.AntiforgeryToken(await browser.GetStringAsync(WebAuthentication.LoginPath)),
        };
        if (keepSignedIn)
        {
            form["Input.KeepSignedIn"] = "true";
        }
        using var response = await browser.PostAsync(WebAuthentication.LoginPath, new FormUrlEncodedContent(form));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }

    private static async Task<SessionStatusResponse?> StatusAsync(HttpClient browser) =>
        await browser.GetFromJsonAsync<SessionStatusResponse>(WebAuthentication.SessionStatusPath);
}
