using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Contracts.Mm.Inventory;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.Inventory;

/// <summary>
/// The global negative stock mode (MM-BR-003, docs/architecture/inventory.md). Any signed-in user can read it;
/// changes need Full Access on the company screen (inventory.md), checked by the screen path policy.
/// </summary>
[ApiController, Route(InventorySettingRoutes.Base)]
public sealed class InventorySettingController(InventorySettingService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Company;

    [HttpGet]
    public Task<InventorySettingDto> Get(CancellationToken cancellationToken) => service.GetAsync(cancellationToken);

    /// <summary>200 with the saved setting, 409 when updatedAt is stale.</summary>
    [HttpPut, Authorize(Policy = Screen)]
    public Task<InventorySettingDto> Update(UpdateInventorySettingRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(request, User.Identity!.Name!, cancellationToken);
}
