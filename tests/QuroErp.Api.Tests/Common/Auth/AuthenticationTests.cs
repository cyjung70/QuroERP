using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests.Common.Auth;

/// <summary>인증·기본 권한 정책 (TASK-CORE-003, security.md).</summary>
public class AuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // 인증 없이는 막혀야 하는, [AllowAnonymous]가 없는 엔드포인트 (개발 환경 OpenAPI 문서)
    private const string ProtectedPath = "/openapi/v1.json";

    [Fact, Trait("Id", "CORE-003-IT-10")]
    public async Task Refresh_NoToken_Returns401ProblemDetails()
    {
        using var response = await factory.CreateClient().PostAsync(AuthRoutes.Refresh, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact, Trait("Id", "CORE-003-IT-11")]
    public async Task FallbackPolicy_EndpointWithoutAuthorizeAttribute_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "CORE-003-IT-12")]
    public async Task Login_CorrectPassword_ReturnsTokenThatAuthenticates()
    {
        var client = factory.CreateClient();

        var login = await LoginAsync(client, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var refresh = await client.PostAsync(AuthRoutes.Refresh, null);
        using var protectedResponse = await client.GetAsync(ProtectedPath);

        login.UserId.Should().Be(ApiFactory.UserId);
        login.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.LoginLogs.Should().Contain(l => l.LoginUserId == ApiFactory.UserId && l.Success && l.AppUserId == 7);
    }

    [Fact, Trait("Id", "CORE-003-IT-13")]
    public async Task Login_WrongPassword_Returns401WithCodeAndLogsFailure()
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            AuthRoutes.Login, new LoginRequest(ApiFactory.UserId, "wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be(AuthCodes.InvalidCredentials);
        factory.LoginLogs.Should().Contain(l => l.LoginUserId == ApiFactory.UserId && !l.Success);
    }

    [Fact, Trait("Id", "CORE-003-IT-14")]
    public async Task Login_MissingPassword_Returns400Auth001()
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(AuthRoutes.Login, new { LoginId = ApiFactory.UserId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.Required);
    }

    [Fact, Trait("Id", "CORE-003-IT-15")]
    public async Task Token_SignedWithOtherKey_Returns401()
    {
        var client = factory.CreateClient();
        var token = (await LoginAsync(client, ApiFactory.Password)).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        using var response = await client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory, Trait("Id", "CORE-003-IT-16")]
    [InlineData(ApiFactory.ApiKey, HttpStatusCode.OK)]
    [InlineData(ApiFactory.RevokedApiKey, HttpStatusCode.Unauthorized)]
    [InlineData("unknown-key", HttpStatusCode.Unauthorized)]
    public async Task ApiKey_Header_AuthenticatesOnlyActiveKey(string apiKey, HttpStatusCode expected)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthRoutes.ApiKeyHeader, apiKey);

        using var response = await client.GetAsync(ProtectedPath);

        response.StatusCode.Should().Be(expected);
    }

    [Fact, Trait("Id", "CORE-003-IT-17")]
    public async Task Refresh_WithApiKey_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthRoutes.ApiKeyHeader, ApiFactory.ApiKey);

        using var response = await client.PostAsync(AuthRoutes.Refresh, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync(AuthRoutes.Login, new LoginRequest(ApiFactory.UserId, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
