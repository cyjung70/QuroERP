using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Items;
using QuroErp.Contracts.Bc.Items;

namespace QuroErp.Api.Bc.Items;

/// <summary>
/// /api/bc/alternate-item-groups (BC-UC-019). Any signed-in user reads; saving is for Full Access, checked by the admin role until the
/// menu policy (security.md "권한") exists, as in ItemController.
/// </summary>
[ApiController, Route(AlternateItemGroupRoutes.Base)]
public sealed class AlternateItemGroupController(AlternateItemGroupService service) : ControllerBase
{
    /// <summary>200 with up to 200 groups in code order, filtered by item class and keyword. 422 Common-E-002 for an unknown item class.</summary>
    [HttpGet]
    public Task<IReadOnlyList<AlternateItemGroupRowDto>> Search([FromQuery] AlternateItemGroupSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>
    /// 200 with the groups of the item class, all rows in one transaction (MM-BR-810, MM-BR-813). 422 MM-E-802, Common-E-002, MM-E-810;
    /// 409 MM-E-814 for a group with items, Common-E-004 for a stale row; 404 Common-E-006.
    /// </summary>
    [HttpPost(AlternateItemGroupRoutes.Batch), Authorize(Roles = "admin")]
    public Task<IReadOnlyList<AlternateItemGroupRowDto>> Save(SaveAlternateItemGroupsRequest request, CancellationToken cancellationToken) =>
        service.SaveAsync(request, User.Identity!.Name!, cancellationToken);
}
