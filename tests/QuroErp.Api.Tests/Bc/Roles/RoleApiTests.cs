using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.Roles;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Contracts.Common.Auth;
using static QuroErp.Api.Tests.Bc.Roles.InMemoryRoleRepository;

namespace QuroErp.Api.Tests.Bc.Roles;

/// <summary>Reads of /api/sys/roles and the Menu policy (BC-UC-005, BC-BR-211, 212, 214). Nothing here changes the shared data.</summary>
public class RoleApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact, Trait("Id", "BC-UT-211")]
    public async Task Roles_ForAnySignedInCaller_ReturnIdAndCodeOfRolesInUse()
    {
        var roles = await factory.NonAdmin().GetFromJsonAsync<List<RoleDto>>(RoleRoutes.Base);

        roles.Should().Equal(new RoleDto(AdminRoleId, "admin"), new RoleDto(UserRoleId, "user"));
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Roles_WithoutSignIn_Return401()
    {
        using var response = await factory.CreateClient().GetAsync(RoleRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UC-005-T03")]
    public async Task Menus_OfARole_InMenuCodeOrder()
    {
        var client = await factory.AdminAsync();

        var menus = await client.GetFromJsonAsync<List<RoleMenuDto>>(MenusPath(AdminRoleId));

        menus!.Select(m => (m.MenuCode, m.FullAccess)).Should().Equal(("ITEM", true), ("ROLEMENU", true));
    }

    [Fact, Trait("Id", "BC-UT-211")]
    public async Task Menus_OfARoleNotInUse_Return400BcE211()
    {
        var client = await factory.AdminAsync();

        using var response = await client.GetAsync(MenusPath(RetiredRoleId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-211");
    }

    [Theory, Trait("Id", "BC-UT-212")]
    [InlineData(null, new[] { "ROLEMENU", "USER" })]
    [InlineData("u", new[] { "USER" })]
    public async Task UnassignedMenus_LeaveOutTheRolesMenus_AndFilterByCodePrefix(string? menuCode, string[] expected)
    {
        var client = await factory.AdminAsync();
        var query = menuCode is null ? "" : $"?menuCode={menuCode}";

        var menus = await client.GetFromJsonAsync<List<MenuOptionDto>>($"{RoleRoutes.Base}/{UserRoleId}/{RoleRoutes.UnassignedMenus}{query}");

        menus!.Select(m => m.MenuCode).Should().Equal(expected);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Changes_WithoutFullAccess_Return403()
    {
        var client = factory.NonAdmin();

        using var save = await client.PutAsJsonAsync(MenusPath(UserRoleId), new SaveRoleMenusRequest([new RoleMenuInsert(UserMenuId, true)], null, null));
        using var delete = await client.DeleteAsync(MenusPath(UserRoleId));

        save.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Roles.Assignments.Should().NotContainKey((UserRoleId, UserMenuId));
    }

    [Theory, Trait("Id", "BC-UT-214")]
    [InlineData("admin", "ROLEMENU", "POST", true)]
    [InlineData("user", "ITEM", "GET", true)]
    [InlineData("user", "ITEM", "HEAD", true)]
    [InlineData("user", "ITEM", "PUT", false)]
    [InlineData("user", "ITEM", "DELETE", false)]
    [InlineData("user", "USER", "GET", false)]
    [InlineData("retired", "ITEM", "GET", false)]
    public async Task MenuPolicy_GetNeedsTheMenu_OtherMethodsNeedFullAccess(string role, string menuCode, string method, bool allowed)
    {
        var policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var policy = (await policies.GetPolicyAsync(MenuPolicy.Prefix + menuCode))!;
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthClaims.Name, "someone"), new Claim(AuthClaims.Role, role), new Claim(AuthClaims.MustChangePassword, "false")], "test",
            AuthClaims.Name, AuthClaims.Role));
        var http = new DefaultHttpContext { Request = { Method = method } };

        var result = await authorization.AuthorizeAsync(user, http, policy);

        result.Succeeded.Should().Be(allowed);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task MenuPolicy_ForAnUnknownMenu_IsDenied()
    {
        var access = await factory.Services.GetRequiredService<MenuPermissionService>().GetAccessAsync("admin", "NOPE", CancellationToken.None);

        access.Should().Be(MenuAccess.None);
    }

    private static string MenusPath(long roleId) => $"{RoleRoutes.Base}/{roleId}/{RoleRoutes.Menus}";
}

/// <summary>PUT and DELETE /api/sys/roles/{roleId}/menus (BC-UC-005, BC-BR-212, 213, 216). Each test has its own host and data.</summary>
public class RoleMenuSaveApiTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact, Trait("Id", "BC-UC-005-T01")]
    public async Task Save_AddsAndChangesFullAccess_AndReturnsTheRolesMenus()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenusPath(UserRoleId), new SaveRoleMenusRequest(
            [new RoleMenuInsert(UserMenuId, false)], [new RoleMenuUpdate(ItemMenuId, true, Start)], null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var menus = (await response.Content.ReadFromJsonAsync<List<RoleMenuDto>>())!;
        menus.Select(m => (m.MenuCode, m.FullAccess)).Should().Equal(("ITEM", true), ("USER", false));
    }

    [Fact, Trait("Id", "BC-UT-212")]
    public async Task Save_AnAssignedMenu_Returns422BcE212()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenusPath(UserRoleId), new SaveRoleMenusRequest([new RoleMenuInsert(ItemMenuId, true)], null, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be("BC-E-212");
        problem.GetProperty("args")[0].GetInt64().Should().Be(ItemMenuId);
    }

    [Fact, Trait("Id", "BC-UC-005-T02")]
    public async Task Save_AStaleRow_Returns409CommonE004()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenusPath(UserRoleId), new SaveRoleMenusRequest(
            null, null, [new RoleMenuDelete(ItemMenuId, Start.AddSeconds(-1))]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "BC-UT-216")]
    public async Task TakingFullAccessToTheRoleMenuScreenFromMyRole_Returns422BcE216()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenusPath(AdminRoleId), new SaveRoleMenusRequest(
            null, [new RoleMenuUpdate(RoleMenuId, false, Start)], null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-216");
    }

    [Fact, Trait("Id", "BC-UT-213")]
    public async Task Delete_RemovesEveryAssignmentOfTheRole_AndReturns204()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.DeleteAsync(MenusPath(UserRoleId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Roles.Assignments.Keys.Should().NotContain(k => k.RoleId == UserRoleId);
    }

    private static string MenusPath(long roleId) => $"{RoleRoutes.Base}/{roleId}/{RoleRoutes.Menus}";
}
