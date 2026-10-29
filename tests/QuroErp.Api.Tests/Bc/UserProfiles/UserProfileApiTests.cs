using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests.Bc.UserProfiles;

/// <summary>GET/PUT /api/bc/user-profile and POST revoke-tokens (BC-UC-003, BC-BR-101~106, BC-BR-403).</summary>
public class UserProfileApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UT-101")]
    public async Task Get_ReturnsTheSignedInUser()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        var profile = await client.GetFromJsonAsync<UserProfileDto>(UserProfileRoutes.Base);

        profile!.UserId.Should().Be(ApiFactory.UserId);
        profile.UserName.Should().Be("Kim");
        profile.DefaultWarehouseCode.Should().Be("W01");
    }

    [Fact, Trait("Id", "BC-UC-003-T01")]
    public async Task Put_SavesMyInfo_AndIgnoresOtherUserIdRoleAndPassword()
    {
        var client = await SignInAsync(ApiFactory.ProfileUserId);
        var loaded = (await client.GetFromJsonAsync<UserProfileDto>(UserProfileRoutes.Base))!;

        // Unknown fields are ignored (api.md); the target is still the signed-in user (BC-BR-101, 102, 103)
        using var response = await client.PutAsJsonAsync(UserProfileRoutes.Base, new
        {
            userName = "Profile Kim", email = "profile@example.com", phone = "010-1111-2222", langCode = "en", defaultWarehouseId = 1,
            loaded.UpdatedAt, userId = ApiFactory.UserId, roleCode = "admin", password = "Other-Pass-1",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = (await response.Content.ReadFromJsonAsync<UserProfileDto>())!;
        saved.Should().BeEquivalentTo(new
        {
            UserId = ApiFactory.ProfileUserId, UserName = "Profile Kim", Email = "profile@example.com", LangCode = "en", DefaultWarehouseId = 1L,
        });
        saved.UpdatedAt.Should().BeAfter(loaded.UpdatedAt);
        factory.UserProfiles.Rows[7].UserName.Should().Be("Kim");
    }

    [Fact, Trait("Id", "BC-UT-103")]
    public void Contract_HasNoPasswordField()
    {
        typeof(UpdateUserProfileRequest).GetProperties().Concat(typeof(UserProfileDto).GetProperties())
            .Should().NotContain(p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact, Trait("Id", "BC-UC-003-T03")]
    public async Task Put_WithAStaleUpdatedAt_Returns409CommonE004()
    {
        var client = await SignInAsync(ApiFactory.ProfileUserId);
        var loaded = (await client.GetFromJsonAsync<UserProfileDto>(UserProfileRoutes.Base))!;

        using var response = await client.PutAsJsonAsync(UserProfileRoutes.Base, Request(loaded) with { UpdatedAt = loaded.UpdatedAt.AddSeconds(-5) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Theory, Trait("Id", "BC-UC-003-T02")]
    [InlineData("", "ko", 1L, "BC-E-102", "UserName")]
    [InlineData("Kim", "ko", 9L, "BC-E-102", "DefaultWarehouseId")]
    [InlineData("Kim", "sw", 1L, "BC-E-405", "sw")]
    public async Task Put_BreakingAFieldRule_Returns422WithCodeAndArg(string name, string lang, long warehouseId, string code, string arg)
    {
        var client = await SignInAsync(ApiFactory.ProfileUserId);
        var loaded = (await client.GetFromJsonAsync<UserProfileDto>(UserProfileRoutes.Base))!;

        using var response = await client.PutAsJsonAsync(
            UserProfileRoutes.Base, Request(loaded) with { UserName = name, LangCode = lang, DefaultWarehouseId = warehouseId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].GetString().Should().Be(arg);
    }

    [Fact, Trait("Id", "BC-UT-104")]
    public async Task PostAndDelete_Return405()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var post = await client.PostAsJsonAsync(UserProfileRoutes.Base, new { userName = "Kim" });
        using var delete = await client.DeleteAsync(UserProfileRoutes.Base);

        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact, Trait("Id", "BC-UT-106")]
    public async Task RevokeTokens_Returns204_RevokesMyAutoLoginTokens_AndKeepsThisSession()
    {
        var client = await SignInAsync(ApiFactory.UserId);
        var now = DateTimeOffset.UtcNow;
        factory.AutoLoginTokens.Tokens["profile-api-mine"] = new AutoLoginTokenRow(901, 7, now.AddDays(10), null, now);
        factory.AutoLoginTokens.Tokens["profile-api-other"] = new AutoLoginTokenRow(902, 11, now.AddDays(10), null, now);

        using var response = await client.PostAsync(UserProfileRoutes.RevokeTokens, null);
        using var refresh = await client.PostAsync(AuthRoutes.Refresh, null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.AutoLoginTokens.Tokens["profile-api-mine"].RevokedAt.Should().NotBeNull();
        factory.AutoLoginTokens.Tokens["profile-api-other"].RevokedAt.Should().BeNull();
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, Trait("Id", "BC-UT-101")]
    public async Task ApiClientAndAnonymous_Return401_AndForcedChangeReturns403Auth005()
    {
        using var apiClient = await factory.NonAdmin().GetAsync(UserProfileRoutes.Base);
        using var anonymous = await factory.CreateClient().GetAsync(UserProfileRoutes.Base);
        using var forced = await (await SignInAsync(ApiFactory.NewUserId)).GetAsync(UserProfileRoutes.Base);

        apiClient.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        forced.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forced.ReadProblemAsync()).GetProperty("code").GetString().Should().Be(AuthCodes.PasswordChangeRequired);
    }

    private static UpdateUserProfileRequest Request(UserProfileDto loaded) =>
        new(loaded.UserName, loaded.Email, loaded.Phone, loaded.LangCode, loaded.DefaultWarehouseId, loaded.UpdatedAt);

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
