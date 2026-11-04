using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.GoodsReceipts;
using QuroErp.Contracts.Mm.GoodsReceipts;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.GoodsReceipts;

/// <summary>
/// Goods receipts (MM-UC-004). Reading needs the goods receipt screen and changes need Full Access on it, checked by the screen
/// path policy (BC-BR-214). Writes answer 409 Common-E-004 when updatedAt is stale.
/// </summary>
[ApiController, Route(GoodsReceiptRoutes.Base)]
public sealed class GoodsReceiptController(GoodsReceiptService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.GoodsReceipt;

    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<GoodsReceiptSummaryDto>> Search([FromQuery] GoodsReceiptSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines. 404 Common-E-006.</summary>
    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<GoodsReceiptDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>PO lines of the vendor's PO and RE orders with remaining qty above 0 (MM-BR-113).</summary>
    [HttpGet(GoodsReceiptRoutes.OpenPoLines), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<OpenPoLineDto>> OpenPoLines([FromQuery] long vendorId, CancellationToken cancellationToken) =>
        service.OpenPoLinesAsync(vendorId, cancellationToken);

    /// <summary>
    /// 201 with the new receipt and its GR number; its stock is received. 422 MM-E-101~703, the inventory checks, Common-E-005/006,
    /// BC-E-412, BC-E-513; 422 MM-W-102 with confirmField confirmOverReceipt in WARN mode (MM-BR-102).
    /// </summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<GoodsReceiptDto>> Create(SaveGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved receipt. 422 MM-E-104 when a line is invoiced or returned, MM-E-107, 116, 117; MM-W-102 as on POST.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<GoodsReceiptDto> Update(long id, SaveGoodsReceiptRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204; the stock and PO received qty are taken back. 422 MM-E-104 when a line is invoiced or returned.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }
}
