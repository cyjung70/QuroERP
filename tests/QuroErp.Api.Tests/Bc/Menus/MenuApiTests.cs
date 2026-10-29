using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuroErp.Contracts.Bc.Menus;
using static QuroErp.Api.Tests.Bc.Menus.InMemoryMenuRepository;

namespace QuroErp.Api.Tests.Bc.Menus;

/// <summary>/api/sys/menus (BC-UC-006, BC-BR-201~207, 214). Saves use their own factory so the shared menus stay as seeded.</summary>
public class MenuApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory, Trait("Id", "BC-UC-006-T03")]
    [InlineData("", new[] { "BASE", "ITEM", "USER" })]
    [InlineData("?menuCode=us", new[] { "USER" })]
    [InlineData("?menuCode=%25", new string[0])]
    public async Task Search_ForAnySignedInCaller_FiltersByCodePrefix(string query, string[] expected)
    {
        var menus = await factory.NonAdmin().GetFromJsonAsync<List<MenuDto>>(MenuRoutes.Base + query);

        menus!.Select(m => m.MenuCode).Should().Equal(expected);
    }

    [Fact, Trait("Id", "BC-UC-006-T03")]
    public async Task Search_ReturnsTheParentCode()
    {
        var menus = await factory.NonAdmin().GetFromJsonAsync<List<MenuDto>>(MenuRoutes.Base + "?menuCode=ITEM");

        menus.Should().ContainSingle().Which.Should().Be(
            new MenuDto(ItemMenuId, "ITEM", "Items", "P", "/bc/item", "bc", "BASE", "Y", Created));
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Search_WithoutSignIn_Returns401()
    {
        using var response = await factory.CreateClient().GetAsync(MenuRoutes.Base);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Id", "BC-UT-214")]
    public async Task Save_WithoutFullAccess_Returns403()
    {
        using var response = await factory.NonAdmin().PutAsJsonAsync(MenuRoutes.Base, new SaveMenusRequest(null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

public class MenuSaveApiTests
{
    [Fact, Trait("Id", "BC-UC-006-T01")]
    public async Task Save_AddsChangesAndDeletes_AndReturnsEveryMenu()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenuRoutes.Base, new SaveMenusRequest(
            [new MenuInsert("master", "Master", "M", null, "bc", "BASE", null), new MenuInsert("whs", "Warehouses", "P", "/bc/whs", "bc", "MASTER", null)],
            [new MenuUpdate(ItemMenuId, "Items", "P", "/bc/item", "bc", "MASTER", "N", Created)],
            [new MenuDelete(UserMenuId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var menus = await response.Content.ReadFromJsonAsync<List<MenuDto>>();
        menus!.Select(m => (m.MenuCode, m.ParentMenuCode, m.ValidFlag)).Should().Equal(
            ("BASE", null, "Y"), ("ITEM", "MASTER", "N"), ("MASTER", "BASE", "Y"), ("WHS", "MASTER", "Y"));
    }

    [Theory, Trait("Id", "BC-UC-006-T02")]
    [InlineData("BC-E-203", "Bad", "M", "/bc/bad")]
    [InlineData("BC-E-201", "Dup", "P", "/bc/dup")]
    public async Task Save_WithAFailingRow_Returns422WithTheRowsCode_AndSavesNothing(string code, string newCode, string type, string programId)
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();
        var insertCode = code == "BC-E-201" ? "item" : newCode;

        using var response = await client.PutAsJsonAsync(MenuRoutes.Base, new SaveMenusRequest(
            [new MenuInsert("OK", "Fine", "P", "/bc/ok", "bc", null, null), new MenuInsert(insertCode, "Name", type, programId, "bc", null, null)],
            null, [new MenuDelete(UserMenuId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("args")[0].GetString().Should().Be(insertCode.ToUpperInvariant());
        var menus = await client.GetFromJsonAsync<List<MenuDto>>(MenuRoutes.Base);
        menus!.Select(m => m.MenuCode).Should().Equal("BASE", "ITEM", "USER");
    }

    [Fact, Trait("Id", "BC-UT-207")]
    public async Task Save_DeletingAMenuAssignedToARole_Returns422BcE207()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenuRoutes.Base, new SaveMenusRequest(null, null, [new MenuDelete(ItemMenuId, Created)]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("BC-E-207");
    }

    [Fact, Trait("Id", "BC-UC-006-T02")]
    public async Task Save_WithAStaleRow_Returns409()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenuRoutes.Base, new SaveMenusRequest(
            null, [new MenuUpdate(UserMenuId, "Users", "P", "/bc/user", "bc", null, null, Created.AddSeconds(-1))], null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-004");
    }

    [Fact, Trait("Id", "BC-UC-006-T02")]
    public async Task Save_WithAnUnknownValidFlag_Returns422CommonE002()
    {
        await using var factory = new ApiFactory();
        var client = await factory.AdminAsync();

        using var response = await client.PutAsJsonAsync(MenuRoutes.Base, new SaveMenusRequest(
            [new MenuInsert("NEW", "New", "P", "/bc/new", "bc", null, "X")], null, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().Should().Be("Common-E-002");
    }
}
