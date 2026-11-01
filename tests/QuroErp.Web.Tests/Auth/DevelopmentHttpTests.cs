using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests.Auth;

public class DevelopmentHttpTests
{
    [Theory]
    [InlineData("DevelopmentHttp", "http", false)]
    [InlineData("DevelopmentHttp", "https", true)]
    [InlineData("Development", "http", true)]
    [InlineData("Production", "http", true)]
    public async Task Login_TransportEnvironment_PreservesCookieSecurityAndHttpSession(
        string environment, string scheme, bool secure)
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        using var factory = new WebFactory
        {
            LoginOptions = new LoginOptionsResponse(true, "ADMIN_ONLY", 30),
            ApiResponse = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new LoginResponse("access", now.AddHours(1), "kim", "Kim", "user",
                    false, "ko", null, null, "remember-token", now.AddDays(14))),
            },
            AuthRouteResponse = path => path.EndsWith(AuthRoutes.Me, StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new MeResponse("kim", "Kim", "user", "ko", false,
                        null, null, null, [], "Quro Co.")),
                }
                : null,
        };
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new TestClock(now)));
        });
        using var browser = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"{scheme}://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var token = WebFactory.AntiforgeryToken(await browser.GetStringAsync(WebAuthentication.LoginPath));

        using var response = await browser.PostAsync(WebAuthentication.LoginPath,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "login",
                ["Input.UserId"] = "kim",
                ["Input.Password"] = "test-password",
                ["Input.KeepSignedIn"] = "true",
                ["__RequestVerificationToken"] = token,
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        foreach (var name in new[] { "QuroErp.Session=", AutoLoginCookie.Name + "=" })
        {
            var cookie = cookies.Should().ContainSingle(c => c.StartsWith(name, StringComparison.Ordinal)).Subject;
            cookie.ToLowerInvariant().Should().Contain("httponly");
            cookie.Split(';').Any(part => part.Trim().Equals("secure", StringComparison.OrdinalIgnoreCase))
                .Should().Be(secure);
        }
        if (environment == WebTransport.HttpDevelopmentEnvironment)
        {
            using var home = await browser.GetAsync("/");
            home.StatusCode.Should().Be(HttpStatusCode.OK);
            home.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
            using var logout = await browser.PostAsync(WebAuthentication.LogoutPath, new FormUrlEncodedContent([]));
            logout.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
