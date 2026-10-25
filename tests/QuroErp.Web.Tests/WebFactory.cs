using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests;

/// <summary>
/// Web 호스트를 메모리에서 띄운다. Api 대신 가짜 응답(ApiResponse)을 돌려주는 HttpMessageHandler를 쓴다.
/// </summary>
public sealed partial class WebFactory : WebApplicationFactory<Program>
{
    /// <summary>인증 API가 돌려줄 응답. 기본은 로그인 실패(401 ProblemDetails, BC-E-001).</summary>
    public Func<HttpResponseMessage> ApiResponse { get; set; } = () => new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent("""{"status":401,"code":"BC-E-001"}""", null, "application/problem+json"),
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebAuthentication.ApiBaseUrlKey, "http://api.test");
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<AuthApiClient>().ConfigurePrimaryHttpMessageHandler(() => new FakeApiHandler(this)));
    }

    /// <summary>쿠키를 유지하고 리디렉션을 따라가지 않는 클라이언트.</summary>
    /// <param name="environment">호스트 환경. 운영(Production)은 오류 처리 미들웨어가 다르다.</param>
    public HttpClient CreateBrowser(string environment = "Development")
    {
        var options = new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true };
        return environment == "Development"
            ? CreateClient(options)
            : WithWebHostBuilder(b => b.UseEnvironment(environment)).CreateClient(options);
    }

    /// <summary>폼의 위조 방지 토큰 값.</summary>
    public static string AntiforgeryToken(string html) =>
        AntiforgeryPattern().Match(html) is { Success: true } m
            ? WebUtility.HtmlDecode(m.Groups[1].Value)
            : throw new InvalidOperationException("위조 방지 토큰이 없다.");

    [GeneratedRegex(@"<input[^>]*name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryPattern();

    private sealed class FakeApiHandler(WebFactory factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(factory.ApiResponse());
    }
}
