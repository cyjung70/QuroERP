using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.PurchaseOrders;

namespace QuroErp.Api.Mm.PurchaseOrders;

/// <summary>
/// Purchase orders (MM-UC-003). Any signed-in user can read; changes need Full Access (BC-BR-214), checked by the admin role
/// until the menu policy (security.md "권한") exists. Writes answer 409 Common-E-004 when updatedAt is stale.
/// </summary>
[ApiController, Route(PurchaseOrderRoutes.Base)]
public sealed class PurchaseOrderController(PurchaseOrderService service) : ControllerBase
{
    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet]
    public Task<IReadOnlyList<PurchaseOrderSummaryDto>> Search([FromQuery] PurchaseOrderSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines. 404 Common-E-006.</summary>
    [HttpGet("{id:long}")]
    public Task<PurchaseOrderDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>201 with a new draft (DR) and its PO number. 422 MM-E-011~031, Common-E-005/006, BC-E-412, BC-E-513.</summary>
    [HttpPost, Authorize(Roles = "admin")]
    public async Task<ActionResult<PurchaseOrderDto>> Create(SavePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved order. 422 MM-E-013 when it is not DR or PO, or something was received.</summary>
    [HttpPut("{id:long}"), Authorize(Roles = "admin")]
    public Task<PurchaseOrderDto> Update(long id, SavePurchaseOrderRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 MM-E-013 when it is not DR or PO, or something was received.</summary>
    [HttpDelete("{id:long}"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, cancellationToken);
        return NoContent();
    }

    /// <summary>DR → PO. 422 MM-E-020 for another status or no lines.</summary>
    [HttpPost("{id:long}/" + PurchaseOrderRoutes.Approve), Authorize(Roles = "admin")]
    public Task<PurchaseOrderDto> Approve(long id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        service.ApproveAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);

    /// <summary>PO or RE → MC. 422 MM-E-022 for another status.</summary>
    [HttpPost("{id:long}/" + PurchaseOrderRoutes.Close), Authorize(Roles = "admin")]
    public Task<PurchaseOrderDto> Close(long id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        service.CloseAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);

    /// <summary>PO with nothing received → CN. 422 MM-E-023 for another status or when something was received.</summary>
    [HttpPost("{id:long}/" + PurchaseOrderRoutes.Cancel), Authorize(Roles = "admin")]
    public Task<PurchaseOrderDto> Cancel(long id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        service.CancelAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);
}
