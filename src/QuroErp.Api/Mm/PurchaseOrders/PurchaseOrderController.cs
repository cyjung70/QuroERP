using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.PurchaseOrders;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.PurchaseOrders;

/// <summary>
/// Purchase orders (MM-UC-003). Reading needs the purchase order screen and changes need Full Access on it, checked by
/// the screen path policy (BC-BR-214). Writes answer 409 Common-E-004 when updatedAt is stale.
/// </summary>
[ApiController, Route(PurchaseOrderRoutes.Base)]
public sealed class PurchaseOrderController(PurchaseOrderService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.PurchaseOrder;

    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<PurchaseOrderSummaryDto>> Search([FromQuery] PurchaseOrderSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines. 404 Common-E-006.</summary>
    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<PurchaseOrderDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>
    /// 201 with a new draft (DR) and its PO number. 422 MM-E-011~031, Common-E-005/006, BC-E-412, BC-E-513; 422 MM-W-016 with
    /// confirmField updateVendorPrice when the vendor price question is not answered (MM-BR-016).
    /// </summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<PurchaseOrderDto>> Create(SavePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved order. 422 MM-E-013 when it is not DR or PO, or something was received; MM-W-016 as on POST.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<PurchaseOrderDto> Update(long id, SavePurchaseOrderRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 MM-E-013 when it is not DR or PO, or something was received.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, cancellationToken);
        return NoContent();
    }

    /// <summary>DR → PO. 422 MM-E-020 for another status or no lines.</summary>
    [HttpPost("{id:long}/" + PurchaseOrderRoutes.Approve), Authorize(Policy = Screen)]
    public Task<PurchaseOrderDto> Approve(long id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        service.ApproveAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);

    /// <summary>PO or RE → MC. 422 MM-E-022 for another status.</summary>
    [HttpPost("{id:long}/" + PurchaseOrderRoutes.Close), Authorize(Policy = Screen)]
    public Task<PurchaseOrderDto> Close(long id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        service.CloseAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);

    /// <summary>PO with nothing received → CN. 422 MM-E-023 for another status or when something was received.</summary>
    [HttpPost("{id:long}/" + PurchaseOrderRoutes.Cancel), Authorize(Policy = Screen)]
    public Task<PurchaseOrderDto> Cancel(long id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        service.CancelAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);
}
