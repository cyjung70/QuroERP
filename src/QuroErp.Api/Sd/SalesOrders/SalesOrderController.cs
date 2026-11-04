using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Sd.SalesOrders;
using QuroErp.Contracts.Sd.SalesOrders;
using QuroErp.Domain.Common.Auth;
using QuroErp.Domain.Sd.SalesOrders;

namespace QuroErp.Api.Sd.SalesOrders;

/// <summary>
/// Sales orders (SD-UC-002). Reading needs the sales order screen and changes need Full Access on it, checked by the screen path
/// policy (BC-BR-214). Writes answer 409 Common-E-004 when updatedAt is stale.
/// </summary>
[ApiController, Route(SalesOrderRoutes.Base)]
public sealed class SalesOrderController(SalesOrderService service, SalesOrderPrintService print) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.SalesOrder;

    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<SalesOrderSummaryDto>> Search([FromQuery] SalesOrderSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines. 404 Common-E-006.</summary>
    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<SalesOrderDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>
    /// 201 with a new SO order and its number (SD-BR-006). 422 SD-E-001~404, Common-E-005/006, BC-E-412; 422 SD-W-009, SD-W-010
    /// or SD-W-309 with confirmField when a save question is not answered (SD-BR-009, SD-BR-010, SD-BR-309).
    /// </summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<SalesOrderDto>> Create(SaveSalesOrderRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved order. 422 SD-E-003 when it is not SO, SD-E-023 for another customer; 409 SD-E-035 (SD-BR-035).</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<SalesOrderDto> Update(long id, SaveSalesOrderRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 SD-E-003 when it is not SO or has a shipment or invoice.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, cancellationToken);
        return NoContent();
    }

    /// <summary>The values of a new line (SD-BR-016, 033, 401, 405). 404 Common-E-006; 422 MM-E-809 for an item not in use.</summary>
    [HttpGet(SalesOrderRoutes.LineDefaults), Authorize(Policy = Screen)]
    public Task<SalesOrderLineDefaultsDto> LineDefaults([FromQuery] SalesOrderLineDefaultsQuery query, CancellationToken cancellationToken) =>
        service.GetLineDefaultsAsync(query, cancellationToken);

    /// <summary>
    /// Lines of a UTF-8 line or barcode file (SD-BR-017~022), or the error rows. Nothing is saved. 422 SD-E-017 for a file over
    /// 1 MB or 1,000 lines.
    /// </summary>
    [HttpPost(SalesOrderRoutes.LineFile), Authorize(Policy = Screen)]
    [RequestSizeLimit(2 * SalesOrderLineFile.MaxBytes + 64 * 1024)]
    public async Task<SalesOrderLineFileResult> LineFile(
        [FromQuery] SalesOrderLineFileQuery query, [FromForm(Name = SalesOrderRoutes.LineFileFormField)] IFormFile file,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(file.OpenReadStream(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var text = await reader.ReadToEndAsync(cancellationToken);
        return await service.ReadLineFileAsync(query, text, file.Length, cancellationToken);
    }

    /// <summary>The sales order form as PDF in the request language. 404 Common-E-006.</summary>
    [HttpGet("{id:long}/" + SalesOrderRoutes.PrintSalesOrder), Authorize(Policy = Screen)]
    public async Task<IActionResult> PrintSalesOrder(long id, CancellationToken cancellationToken) =>
        Download(await print.SalesOrderPdfAsync(id, CultureInfo.CurrentUICulture, cancellationToken));

    /// <summary>The picking list as PDF. 404 Common-E-006.</summary>
    [HttpGet("{id:long}/" + SalesOrderRoutes.PrintPickingList), Authorize(Policy = Screen)]
    public async Task<IActionResult> PrintPickingList(long id, CancellationToken cancellationToken) =>
        Download(await print.PickingListPdfAsync(id, CultureInfo.CurrentUICulture, cancellationToken));

    /// <summary>The set component list as PDF. 404 Common-E-006.</summary>
    [HttpGet("{id:long}/" + SalesOrderRoutes.PrintSetComponentList), Authorize(Policy = Screen)]
    public async Task<IActionResult> PrintSetComponentList(long id, CancellationToken cancellationToken) =>
        Download(await print.SetComponentListPdfAsync(id, CultureInfo.CurrentUICulture, cancellationToken));

    private FileContentResult Download(PrintFile file) => File(file.Content, file.ContentType, file.FileName);
}
