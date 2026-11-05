using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Sd.Shipments;
using QuroErp.Contracts.Sd.Shipments;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Sd.Shipments;

/// <summary>
/// Shipments (SD-UC-051). Reading needs the shipment screen and changes need Full Access on it, checked by the screen path policy
/// (BC-BR-214). Writes answer 409 Common-E-004 when updatedAt is stale.
/// </summary>
[ApiController, Route(ShipmentRoutes.Base)]
public sealed class ShipmentController(ShipmentService service, ShipmentPrintService print) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Shipment;

    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<ShipmentSummaryDto>> Search([FromQuery] ShipmentSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines. 404 Common-E-006.</summary>
    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<ShipmentDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>
    /// 201 with a new shipment and its number (SD-BR-164); stock is issued and a SHIP_AND_BILL shipment makes its invoice
    /// (SD-BR-163, SD-BR-167). 422 SD-E-161, 162, 114, 170, 174, 175, the inventory checks, Common-E-005/006; 422 SD-W-309 with
    /// confirmField (SD-BR-309).
    /// </summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<ShipmentDto>> Create(SaveShipmentRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved shipment. 422 SD-E-104, SD-E-107, SD-E-168, SD-E-023 and the checks of a new shipment.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<ShipmentDto> Update(long id, SaveShipmentRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>200 with the void shipment (SD-BR-169, SD-BR-177). 422 SD-E-104, SD-E-168.</summary>
    [HttpPost("{id:long}/" + ShipmentRoutes.Void), Authorize(Policy = Screen)]
    public Task<ShipmentDto> Void(long id, VoidShipmentRequest request, CancellationToken cancellationToken) =>
        service.VoidAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);

    /// <summary>The customer's orders with lines left to ship (SD-BR-172).</summary>
    [HttpGet(ShipmentRoutes.OpenOrderLines), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<OpenSalesOrderDto>> OpenOrderLines([FromQuery] long customerId, CancellationToken cancellationToken) =>
        service.OpenOrderLinesAsync(customerId, cancellationToken);

    /// <summary>SHIP_THEN_BILL lines of the customer in this currency left to bill (SD-BR-115), for the invoice screen.</summary>
    [HttpGet(ShipmentRoutes.UnbilledLines), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<UnbilledShipmentLineDto>> UnbilledLines(
        [FromQuery] long customerId, [FromQuery] string currencyCode, CancellationToken cancellationToken) =>
        service.UnbilledLinesAsync(customerId, currencyCode, cancellationToken);

    /// <summary>BILL_THEN_SHIP invoice lines of the customer left to ship (SD-BR-175).</summary>
    [HttpGet(ShipmentRoutes.UnshippedInvoiceLines), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<UnshippedInvoiceLineDto>> UnshippedInvoiceLines([FromQuery] long customerId, CancellationToken cancellationToken) =>
        service.UnshippedInvoiceLinesAsync(customerId, cancellationToken);

    /// <summary>The delivery note as PDF in the request language. 404 Common-E-006.</summary>
    [HttpGet("{id:long}/" + ShipmentRoutes.Print), Authorize(Policy = Screen)]
    public async Task<IActionResult> Print(long id, CancellationToken cancellationToken)
    {
        var file = await print.DeliveryNotePdfAsync(id, CultureInfo.CurrentUICulture, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
