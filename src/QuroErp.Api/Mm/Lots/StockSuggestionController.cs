using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Mm.Lots;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Api.Mm.Lots;

/// <summary>
/// FIFO suggestion of the Locations and Lots of a line that decreases stock (TASK-CORE-068, MM-BR-716). Any signed-in user or API
/// client can read it (FallbackPolicy).
/// </summary>
[ApiController, Route(LotRoutes.Suggestions)]
public sealed class StockSuggestionController(LotService service) : ControllerBase
{
    /// <summary>
    /// Where qty of the item should leave the warehouse: (Location, Lot) parts in FIFO order, Lot '*' first, then received date,
    /// Lot number and Location code. The parts add up to less than qty when the stock is short. Date defaults to today.
    /// </summary>
    /// <response code="422">Common-E-005 when itemId, warehouseId or a qty above zero is missing.</response>
    [HttpGet]
    public Task<IReadOnlyList<StockSuggestionDto>> Suggest([FromQuery] StockSuggestionQuery query, CancellationToken cancellationToken) =>
        service.SuggestAsync(query, cancellationToken);
}
