using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Roles;
using QuroErp.Contracts.Bc.Roles;

namespace QuroErp.Api.Bc.Roles;

/// <summary>
/// Roles in use (role-menu spec, GET /api/sys/roles). Any signed-in user or API client can read it (FallbackPolicy).
/// Menu assignment endpoints come with the role-menu task (TASK-BC-009).
/// </summary>
[ApiController, Route(RoleRoutes.Base)]
public sealed class RoleController(RoleService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RoleDto>> GetInUse(CancellationToken cancellationToken) => service.GetInUseAsync(cancellationToken);
}
