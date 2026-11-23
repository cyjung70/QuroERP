using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.StockTransfers;
using QuroErp.Contracts.Mm.StockTransfers;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.StockTransfers;

/// <summary>
/// Stock transfers TR and LT (MM-UC-018; the PDA move MM-UC-039 saves through the same POST). Any signed-in user can read;
/// changes need Full Access on the stock transfer screen, checked by the screen path policy (BC-BR-214).
/// </summary>
[ApiController]
public sealed class StockTransferController(StockTransferService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.StockTransfer;

    /// <summary>Newest first, at most 1000 rows. 422 Common-E-002 for a movement type other than TR or LT.</summary>
    [HttpGet(StockTransferRoutes.Base), Authorize]
    public Task<IReadOnlyList<StockTransferSummaryDto>> Search([FromQuery] StockTransferSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines. 404 Common-E-006.</summary>
    [HttpGet(StockTransferRoutes.ById), Authorize]
    public Task<StockTransferDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>
    /// 201: issues every line from the sending side and receives it at the receiving side (MM-BR-705~708, MM-BR-715). 422
    /// Common-E-005/002, MM-E-705, 710, 713, 715, 912 and the inventory checks (MM-E-001, 002, 009, 010, 355, 356, 714, 916).
    /// </summary>
    [HttpPost(StockTransferRoutes.Base), Authorize(Policy = Screen)]
    public async Task<ActionResult<StockTransferDto>> Create(SaveStockTransferRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Header.Id }, dto);
    }

    /// <summary>
    /// Reverses the transfer and saves the request under its number (MM-BR-707). 404 Common-E-006; 409 Common-E-004 when stale; 422
    /// as on create, MM-E-002 for a closed month, MM-E-912 for a Pallet changed since.
    /// </summary>
    [HttpPut(StockTransferRoutes.ById), Authorize(Policy = Screen)]
    public Task<StockTransferDto> Update(long id, SaveStockTransferRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204: reverses and deletes the transfer (MM-BR-707). 404 Common-E-006; 409 Common-E-004; 422 MM-E-002, MM-E-912, the inventory checks.</summary>
    [HttpDelete(StockTransferRoutes.ById), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }
}
