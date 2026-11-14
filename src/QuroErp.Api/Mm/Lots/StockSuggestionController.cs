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
    /// Lot number and Location code. When the stock is short the lines add up to less than qty and shortQty is the rest (0 when
    /// covered). Date defaults to today.
    /// </summary>
    /// <response code="422">Common-E-005 when itemId, warehouseId or a qty above zero is missing.</response>
    [HttpGet]
    public Task<StockSuggestionResultDto> Suggest([FromQuery] StockSuggestionQuery query, CancellationToken cancellationToken) =>
        service.SuggestAsync(query, cancellationToken);
}
