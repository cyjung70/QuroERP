using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.Roles;
using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Api.Bc.Roles;

/// <summary>
/// Roles in use and role menu permissions (BC-UC-005). Any signed-in user or API client can read (FallbackPolicy); changes need
/// Full Access (BC-BR-214), checked by the admin role until the menu data is loaded, as on the other screens.
/// </summary>
[ApiController, Route(RoleRoutes.Base)]
public sealed class RoleController(RoleService service) : ControllerBase
{
    private const string AdminRole = "admin";

    [HttpGet]
    public Task<IReadOnlyList<RoleDto>> GetInUse(CancellationToken cancellationToken) => service.GetInUseAsync(cancellationToken);

    /// <summary>400 BC-E-211 when the role is missing or not in use.</summary>
    [HttpGet("{roleId:long}/" + RoleRoutes.Menus)]
    public Task<IReadOnlyList<RoleMenuDto>> GetMenus(long roleId, CancellationToken cancellationToken) =>
        service.GetMenusAsync(roleId, cancellationToken);

    /// <summary>Menus the role does not have yet whose code starts with menuCode (BC-BR-212).</summary>
    [HttpGet("{roleId:long}/" + RoleRoutes.UnassignedMenus)]
    public Task<IReadOnlyList<MenuOptionDto>> GetUnassignedMenus(long roleId, [FromQuery] string? menuCode, CancellationToken cancellationToken) =>
        service.GetUnassignedMenusAsync(roleId, menuCode, cancellationToken);

    /// <summary>
    /// 200 with the role's menus after the save. Nothing is saved when a row fails: 422 BC-E-212 or BC-E-216, 404 Common-E-006 for an
    /// unknown menu, 409 Common-E-004 when a row changed after it was read, 400 BC-E-211.
    /// </summary>
    [HttpPut("{roleId:long}/" + RoleRoutes.Menus), Authorize(Roles = AdminRole)]
    public Task<IReadOnlyList<RoleMenuDto>> SaveMenus(long roleId, SaveRoleMenusRequest request, CancellationToken cancellationToken) =>
        service.SaveMenusAsync(roleId, request, User.FindFirst(AuthClaims.Role)?.Value, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 BC-E-216 when it would remove the last Full Access to the role menu screen, 400 BC-E-211.</summary>
    [HttpDelete("{roleId:long}/" + RoleRoutes.Menus), Authorize(Roles = AdminRole)]
    public async Task<IActionResult> DeleteMenus(long roleId, CancellationToken cancellationToken)
    {
        await service.DeleteMenusAsync(roleId, User.FindFirst(AuthClaims.Role)?.Value, cancellationToken);
        return NoContent();
    }
}
