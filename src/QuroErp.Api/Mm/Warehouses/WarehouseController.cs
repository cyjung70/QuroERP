using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Warehouses;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.Warehouses;

/// <summary>
/// Warehouse master (MM-UC-017) and the warehouse choice list (TASK-CORE-022). Any signed-in user or API client can read
/// (FallbackPolicy); changes need Full Access on the screen (BC-BR-214), checked by the screen path policy.
/// </summary>
[ApiController]
public sealed class WarehouseController(WarehouseService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Warehouse;

    /// <summary>Every warehouse in code order.</summary>
    /// <param name="includeInactive">true adds warehouses not in use.</param>
    [HttpGet(WarehouseRoutes.Base)]
    public Task<IReadOnlyList<WarehouseDto>> Search([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        service.SearchAsync(includeInactive, cancellationToken);

    /// <param name="includeConsignment">true adds the consignment warehouse, for report conditions only (MM-BR-710).</param>
    /// <param name="includeInactive">true adds warehouses not in use, for report and search filters.</param>
    [HttpGet(WarehouseRoutes.Options)]
    public Task<IReadOnlyList<WarehouseOptionDto>> GetOptions(
        [FromQuery] bool includeConsignment, [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        service.GetOptionsAsync(includeConsignment, includeInactive, cancellationToken);

    /// <summary>
    /// 200 with every warehouse after the save. Nothing is saved when a row fails: 422 MM-E-701 (code or name, a duplicate code,
    /// a changed code), MM-E-702 (not exactly one default warehouse in use), MM-E-704 with the code and ONHAND, FUTURE_MOVEMENT,
    /// HAS_MOVEMENT or IN_USE, MM-E-709 (type, consignment warehouse); 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPost(WarehouseRoutes.Batch), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<WarehouseDto>> Save(BatchRequest<WarehouseSaveRow, WarehouseKey> request, CancellationToken cancellationToken) =>
        service.SaveAsync(request, User.Identity!.Name!, cancellationToken);
}
