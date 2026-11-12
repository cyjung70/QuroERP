using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Application.Sd.SalesInvoices;
using QuroErp.Contracts.Sd.SalesInvoices;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Sd.SalesInvoices;

/// <summary>
/// Sales invoices (SD-UC-003). Reading needs the sales invoice screen and changes need Full Access on it, checked by the screen
/// path policy (BC-BR-214). Writes answer 409 Common-E-004 when updatedAt is stale. Credit Memo ids answer 404 (SD-BR-120).
/// </summary>
[ApiController, Route(SalesInvoiceRoutes.Base)]
public sealed class SalesInvoiceController(SalesInvoiceService service, SalesInvoicePrintService print) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.SalesInvoice;

    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<SalesInvoiceSummaryDto>> Search([FromQuery] SalesInvoiceSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header, lines and cancellations. 404 Common-E-006.</summary>
    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<SalesInvoiceDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>
    /// 201 with a new invoice and its number (SD-BR-106). 422 SD-E-101, 102, 103, 116, 131, 141, 404, MM-E-703, MM-E-710, the
    /// inventory checks of BILL_THEN_SHIP lines, Common-E-005/006; 422 SD-W-009 or SD-W-309 with confirmField (SD-BR-117, SD-BR-309).
    /// </summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<SalesInvoiceDto>> Create(SaveSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved invoice. 422 SD-E-104, 107, 119, 136, 144 and the checks of a new invoice.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<SalesInvoiceDto> Update(long id, SaveSalesInvoiceRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. 422 SD-E-104, SD-E-113, SD-E-144.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }

    /// <summary>200 with the void invoice (SD-BR-113). 422 SD-E-104, SD-E-113, SD-E-144.</summary>
    [HttpPost("{id:long}/" + SalesInvoiceRoutes.Void), Authorize(Policy = Screen)]
    public Task<SalesInvoiceDto> Void(long id, VoidSalesInvoiceRequest request, CancellationToken cancellationToken) =>
        service.VoidAsync(id, request.UpdatedAt, User.Identity!.Name!, cancellationToken);

    /// <summary>BILL_THEN_SHIP order lines of the customer in this currency left to bill (SD-BR-141).</summary>
    [HttpGet(SalesInvoiceRoutes.OpenOrderLines), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<OpenBillFirstOrderLineDto>> OpenOrderLines(
        [FromQuery] long customerId, [FromQuery] string currencyCode, CancellationToken cancellationToken) =>
        service.OpenOrderLinesAsync(customerId, currencyCode, cancellationToken);

    /// <summary>201 with the Credit Memo the cancellation made (SD-BR-145). 422 SD-E-145.</summary>
    [HttpPost("{id:long}/" + SalesInvoiceRoutes.CancelUnshipped), Authorize(Policy = Screen)]
    public async Task<ActionResult<CancelUnshippedResult>> CancelUnshipped(long id, CancelUnshippedRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CancelUnshippedAsync(id, request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, result);
    }

    /// <summary>204: the cancellation is undone and its Credit Memo deleted (SD-BR-146). 422 SD-E-146; 404 when it is not a cancellation of this invoice.</summary>
    [HttpDelete("{id:long}/" + SalesInvoiceRoutes.Cancellations + "/{creditMemoId:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> UndoCancellation(
        long id, long creditMemoId, [FromQuery] DateTimeOffset? updatedAt, CancellationToken cancellationToken)
    {
        await service.UndoCancellationAsync(id, creditMemoId, updatedAt, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }

    /// <summary>The sales invoice as PDF in the request language. 404 Common-E-006.</summary>
    [HttpGet("{id:long}/" + SalesInvoiceRoutes.PrintInvoice), Authorize(Policy = Screen)]
    public async Task<IActionResult> PrintInvoice(long id, CancellationToken cancellationToken) =>
        Pdf(await print.SalesInvoicePdfAsync(id, CultureInfo.CurrentUICulture, cancellationToken));

    /// <summary>The KR tax invoice as PDF (SD-BR-134). 422 SD-E-134; 404 for a company outside KR.</summary>
    [HttpGet("{id:long}/" + SalesInvoiceRoutes.PrintTaxInvoice), Authorize(Policy = Screen)]
    public async Task<IActionResult> PrintTaxInvoice(long id, CancellationToken cancellationToken) =>
        Pdf(await print.TaxInvoicePdfAsync(id, CultureInfo.CurrentUICulture, cancellationToken));

    private FileContentResult Pdf(PrintFile file) => File(file.Content, file.ContentType, file.FileName);
}
