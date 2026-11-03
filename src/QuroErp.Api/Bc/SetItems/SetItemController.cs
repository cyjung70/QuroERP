using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.SetItems;
using QuroErp.Contracts.Bc.SetItems;
using QuroErp.Contracts.Common;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Bc.SetItems;

/// <summary>
/// Set item composition (BC-UC-013). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access
/// on the set item screen (BC-BR-214), checked by the screen path policy (security.md "권한").
/// </summary>
[ApiController, Route(SetItemRoutes.Base)]
public sealed class SetItemController(SetItemService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.SetItem;

    /// <summary>The set and its components in sequence order. 422 Common-E-005, SD-E-501.</summary>
    [HttpGet]
    public Task<SetItemDto> Get([FromQuery] string? itemCd, CancellationToken cancellationToken) =>
        service.GetAsync(itemCd, cancellationToken);

    /// <summary>
    /// 200 with the set after the save. Nothing is saved when a row fails (SD-BR-506): 422 SD-E-501, SD-E-502, MM-E-361, SD-E-503,
    /// SD-E-504 or BC-E-423 with the row's item code, 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPost(SetItemRoutes.Batch), Authorize(Policy = Screen)]
    public Task<SetItemDto> Save(
        [FromQuery] string? itemCd, BatchRequest<SetComponentRowRequest, SetComponentDeleteRequest> request,
        CancellationToken cancellationToken) =>
        service.SaveAsync(itemCd, request, User.Identity!.Name!, cancellationToken);
}
