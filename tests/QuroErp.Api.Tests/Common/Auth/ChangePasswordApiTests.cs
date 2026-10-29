using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests.Common.Auth;

/// <summary>POST /api/auth/change-password and the token version on refresh (BC-UC-002, BC-BR-051~059, BC-BR-130).</summary>
public class ChangePasswordApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ProtectedPath = "/openapi/v1.json";

    [Fact, Trait("Id", "BC-UC-002-T01")]
    public async Task ForcedChange_Returns200WithNewToken_ThatOpensOtherApis()
    {
        var client = await SignInAsync(ApiFactory.ForcedChangeUserId);

        using var response = await client.PostAsJsonAsync(AuthRoutes.ChangePassword, new ChangePasswordRequest(ApiFactory.Password, "Spring-2026"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        changed.MustChangePassword.Should().BeFalse();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changed.AccessToken);
        using var other = await client.GetAsync(ProtectedPath);
        other.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory, Trait("Id", "BC-UC-002-T02")]
    [InlineData(null, "Spring-2026", "AUTH-001", "Please enter your current password.")]
    [InlineData("wrong", "Spring-2026", "AUTH-015", "Current password is incorrect.")]
    [InlineData(ApiFactory.Password, "Ab1!", "AUTH-011", "Password must be 8 to 128 characters long.")]
    [InlineData(ApiFactory.Password, "abcdefgh1", "AUTH-012", "Use at least 3 of: uppercase, lowercase, digits, symbols.")]
    [InlineData(ApiFactory.Password, ApiFactory.Password, "AUTH-013", "You cannot reuse a recent password.")]
    public async Task InvalidRequest_Returns400WithCodeAndMessage(string? current, string next, string code, string message)
    {
        var client = await SignInAsync(ApiFactory.ChangeUserId);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        using var response = await client.PostAsJsonAsync(AuthRoutes.ChangePassword, new ChangePasswordRequest(current, next));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("detail").GetString().Should().Be(message);
    }

    [Fact, Trait("Id", "BC-UT-130")]
    public async Task Refresh_WithTheTokenFromBeforeTheChange_Returns401Auth006()
    {
        var client = await SignInAsync(ApiFactory.StaleUserId);
        var oldToken = client.DefaultRequestHeaders.Authorization!.Parameter;
        using var change = await client.PostAsJsonAsync(AuthRoutes.ChangePassword, new ChangePasswordRequest(ApiFactory.Password, "Spring-2026"));
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;

        using var stale = await client.PostAsync(AuthRoutes.Refresh, null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changed.AccessToken);
        using var fresh = await client.PostAsync(AuthRoutes.Refresh, null);

        oldToken.Should().NotBe(changed.AccessToken);
        stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await stale.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.SessionExpired);
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, Trait("Id", "BC-UT-051")]
    public async Task Anonymous_Returns401()
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            AuthRoutes.ChangePassword, new ChangePasswordRequest(ApiFactory.Password, "Spring-2026"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> SignInAsync(string userId)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(AuthRoutes.Login, new LoginRequest(userId, ApiFactory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
}
