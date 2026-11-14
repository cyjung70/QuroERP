using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Mm.Lots;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Api.Mm.Lots;

/// <summary>
/// Lot search of the stock document screens (TASK-CORE-027, MM-BR-362). Any signed-in user or API client can read it
/// (FallbackPolicy).
/// </summary>
[ApiController, Route(LotRoutes.Base)]
public sealed class LotController(LotService service) : ControllerBase
{
    /// <summary>
    /// The (Location, Lot) balances of the item in the warehouse above zero at the end of the date, in FIFO order: Lot '*' first,
    /// then received date, Lot number and Location code (MM-BR-362).
    /// </summary>
    /// <response code="422">Common-E-005 when itemId, warehouseId or date is missing.</response>
    [HttpGet]
    public Task<IReadOnlyList<LotBalanceDto>> Search([FromQuery] LotSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);
}
