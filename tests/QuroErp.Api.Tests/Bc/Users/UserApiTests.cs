using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Users;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Api.Tests.Bc.Users;

/// <summary>/api/bc/users (BC-UC-004, BC-BR-121~132).</summary>
public class UserApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string NewPassword = "Reset-Pass-9";

    private static string Path(long id, string? action = null) =>
        action is null ? $"{UserRoutes.Base}/{id}" : $"{UserRoutes.Base}/{id}/{action}";

    [Fact, Trait("Id", "BC-UT-132")]
    public async Task NonAdminUser_Gets403_OnReadsAndChanges()
    {
        var client = await SignInAsync(ApiFactory.ProfileUserId);

        using var list = await client.GetAsync(UserRoutes.Base);
        using var unlock = await client.PostAsync(Path(InMemoryUserRepository.TargetId, UserRoutes.Unlock), null);

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unlock.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Id", "BC-UT-132")]
    public async Task Anonymous_Gets401()
    {
        using var response = await factory.CreateClient().GetAsync(UserRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UT-132")]
    public async Task Search_ReturnsUsersInUserIdOrder_WithoutPasswordHash()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.GetAsync($"{UserRoutes.Base}?userId=t");
        var body = await response.Content.ReadAsStringAsync();
        var list = (await response.Content.ReadFromJsonAsync<UserListDto>())!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        list.Items.Should().ContainSingle(u => u.Id == InMemoryUserRepository.TargetId);
        body.Should().NotContainEquivalentOf("password_hash").And.NotContainEquivalentOf("passwordHash");
    }

    [Fact, Trait("Id", "BC-UC-004")]
    public async Task Get_MissingUser_Returns404CommonE006()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.GetAsync(Path(999));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-006");
    }

    [Fact, Trait("Id", "BC-UC-004")]
    public async Task PreviousAndNext_FollowUserIdOrder_And204AtTheEnds()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        var previous = await client.GetFromJsonAsync<UserDto>(Path(InMemoryUserRepository.TargetId, UserRoutes.Previous));
        using var last = await client.GetAsync(Path(InMemoryUserRepository.TargetId, UserRoutes.Next));

        previous!.UserId.Should().Be(ApiFactory.ProfileUserId);
        last.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Id", "BC-UC-004-T01")]
    public async Task Create_Returns201_WithANormalizedUserIdThatMustChangeThePassword()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.PostAsJsonAsync(UserRoutes.Base, new CreateUserRequest(
            "  Api.New ", NewPassword, "Api New", InMemoryUserRepository.UserRoleId, null, null, "ko", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var saved = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        saved.UserId.Should().Be("api.new");
        saved.ValidFlag.Should().Be("Y");
        saved.MustChangePassword.Should().BeTrue();
        response.Headers.Location!.ToString().Should().EndWith(Path(saved.Id));
    }

    [Fact, Trait("Id", "BC-UT-121")]
    public async Task Create_WithATakenUserId_Returns422BcE121()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.PostAsJsonAsync(UserRoutes.Base, new CreateUserRequest(
            "TARGET", NewPassword, "Dup", InMemoryUserRepository.UserRoleId, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-121");
    }

    [Fact, Trait("Id", "BC-UT-123")]
    public async Task Create_WithAWeakPassword_Returns400AuthCode()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.PostAsJsonAsync(UserRoutes.Base, new CreateUserRequest(
            "weak.pass", "short", "Weak", InMemoryUserRepository.UserRoleId, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().StartWith("AUTH-");
    }

    [Fact, Trait("Id", "BC-UC-004-T03")]
    public async Task Put_WithAStaleUpdatedAt_Returns409CommonE004()
    {
        var client = await SignInAsync(ApiFactory.UserId);
        var loaded = (await client.GetFromJsonAsync<UserDto>(Path(InMemoryUserRepository.TargetId)))!;

        using var response = await client.PutAsJsonAsync(Path(loaded.Id), Request(loaded) with { UpdatedAt = loaded.UpdatedAt.AddSeconds(-5) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "BC-UT-131")]
    public async Task Put_ChangingMyOwnRole_Returns422BcE131()
    {
        var client = await SignInAsync(ApiFactory.UserId);
        var me = (await client.GetFromJsonAsync<UserDto>(Path(7)))!;

        using var response = await client.PutAsJsonAsync(Path(7), Request(me) with { RoleId = InMemoryUserRepository.UserRoleId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-131");
    }

    [Fact, Trait("Id", "BC-UT-125")]
    public async Task Delete_IsNotAllowed_Returns405()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.DeleteAsync(Path(InMemoryUserRepository.TargetId));

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact, Trait("Id", "BC-UT-128")]
    public async Task Unlock_Returns204_AndClearsTheLock()
    {
        var client = await SignInAsync(ApiFactory.UserId);
        const long lockedId = 31;
        factory.Users.Rows[lockedId] = InMemoryUserRepository.Row(lockedId, "locked", InMemoryUserRepository.UserRoleId,
            lockedUntil: DateTimeOffset.UtcNow.AddHours(1));

        using var response = await client.PostAsync(Path(lockedId, UserRoutes.Unlock), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Users.Rows[lockedId].LockedUntil.Should().BeNull();
        factory.Users.Rows[lockedId].FailedCount.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-129")]
    public async Task ResetPassword_Returns204_AndRaisesTheTokenVersion()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.PostAsJsonAsync(
            Path(InMemoryUserRepository.TargetId, UserRoutes.ResetPassword), new ResetPasswordRequest(NewPassword));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Users.Rows[InMemoryUserRepository.TargetId].MustChangePassword.Should().BeTrue();
        factory.Users.TokenVersionRaised.Should().Contain(InMemoryUserRepository.TargetId);
    }

    [Fact, Trait("Id", "BC-UT-129")]
    public async Task ResetPassword_WithoutAPassword_Returns400Auth001()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.PostAsJsonAsync(
            Path(InMemoryUserRepository.TargetId, UserRoutes.ResetPassword), new ResetPasswordRequest(null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("AUTH-001");
    }

    [Fact, Trait("Id", "BC-UT-130")]
    public async Task RevokeTokens_Returns204()
    {
        var client = await SignInAsync(ApiFactory.UserId);

        using var response = await client.PostAsync(Path(InMemoryUserRepository.TargetId, UserRoutes.RevokeTokens), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Users.TokenVersionRaised.Should().Contain(InMemoryUserRepository.TargetId);
    }

    private static UpdateUserRequest Request(UserDto loaded) =>
        new(loaded.UserName, loaded.RoleId, loaded.Email, loaded.Phone, loaded.LangCode, loaded.UserValidDt, loaded.ValidFlag, loaded.UpdatedAt);

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
