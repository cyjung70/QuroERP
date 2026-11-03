using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.Menus;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Bc.Menus;

/// <summary>
/// Company menus (BC-UC-006). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access on the screen,
/// checked by the screen path policy (BC-BR-214).
/// </summary>
[ApiController, Route(MenuRoutes.Base)]
public sealed class MenuController(MenuService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.CompanyMenu;

    [HttpGet]
    public Task<IReadOnlyList<MenuDto>> Search([FromQuery] string? menuCode, CancellationToken cancellationToken) =>
        service.SearchAsync(menuCode, cancellationToken);

    /// <summary>
    /// 200 with every menu after the save. Nothing is saved when a row fails: 422 BC-E-201~205, BC-E-207 or Common-E-002 with the
    /// row's menu code, 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPut, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<MenuDto>> Save(SaveMenusRequest request, CancellationToken cancellationToken) =>
        service.SaveAsync(request, User.Identity!.Name!, cancellationToken);
}
