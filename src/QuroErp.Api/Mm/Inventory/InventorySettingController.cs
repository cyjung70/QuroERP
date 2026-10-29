using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Mm.Inventory;
using QuroErp.Contracts.Mm.Inventory;

namespace QuroErp.Api.Mm.Inventory;

/// <summary>The global negative stock mode (MM-BR-003, docs/architecture/inventory.md). Any signed-in user can read it; only admin can change it.</summary>
[ApiController, Route(InventorySettingRoutes.Base)]
public sealed class InventorySettingController(InventorySettingService service) : ControllerBase
{
    [HttpGet]
    public Task<InventorySettingDto> Get(CancellationToken cancellationToken) => service.GetAsync(cancellationToken);

    /// <summary>200 with the saved setting, 409 when updatedAt is stale.</summary>
    [HttpPut, Authorize(Roles = "admin")]
    public Task<InventorySettingDto> Update(UpdateInventorySettingRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(request, User.Identity!.Name!, cancellationToken);
}
