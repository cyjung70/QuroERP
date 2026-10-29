using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Mm.Warehouses;
using QuroErp.Contracts.Mm.Warehouses;

namespace QuroErp.Api.Mm.Warehouses;

/// <summary>
/// Warehouse choice list (TASK-CORE-022). Any signed-in user or API client can read it (FallbackPolicy).
/// The warehouse master list and batch save come with the warehouse master task.
/// </summary>
[ApiController, Route(WarehouseRoutes.Base)]
public sealed class WarehouseController(WarehouseService service) : ControllerBase
{
    [HttpGet("options")]
    public Task<IReadOnlyList<WarehouseOptionDto>> GetOptions(CancellationToken cancellationToken) => service.GetOptionsAsync(cancellationToken);
}
