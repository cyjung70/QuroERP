using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Items;
using QuroErp.Contracts.Bc.Items;

namespace QuroErp.Api.Bc.Items;

/// <summary>
/// /api/bc/alternate-items (BC-UC-012). Any signed-in user reads; saving is for Full Access, checked by the admin role until the menu
/// policy (security.md "권한") exists, as in ItemController.
/// </summary>
[ApiController, Route(AlternateItemRoutes.Base)]
public sealed class AlternateItemController(AlternateItemService service) : ControllerBase
{
    /// <summary>200 with the group and its items in item code order. 422 MM-E-802 without a group code, 404 Common-E-006.</summary>
    [HttpGet]
    public Task<AlternateItemGroupDto> Get([FromQuery] AlternateItemQuery query, CancellationToken cancellationToken) =>
        service.GetAsync(query, cancellationToken);

    /// <summary>
    /// 200 with the saved group, all rows in one transaction (MM-BR-811~813). 422 MM-E-802, MM-E-812; 409 MM-E-812 for a move that is
    /// not confirmed, Common-E-004 for a stale screen; 404 Common-E-006.
    /// </summary>
    [HttpPost(AlternateItemRoutes.Batch), Authorize(Roles = "admin")]
    public Task<AlternateItemGroupDto> Save(SaveAlternateItemsRequest request, CancellationToken cancellationToken) =>
        service.SaveAsync(request, User.Identity!.Name!, cancellationToken);
}
