using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.SupplierInvoices;
using QuroErp.Contracts.Mm.SupplierInvoices;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.SupplierInvoices;

/// <summary>
/// Supplier invoices (MM-UC-032). Reading needs the supplier invoice screen and changes need Full Access on it, checked by the
/// screen path policy (BC-BR-214). Writes answer 409 Common-E-004 when updatedAt is stale.
/// </summary>
[ApiController, Route(SupplierInvoiceRoutes.Base)]
public sealed class SupplierInvoiceController(SupplierInvoiceService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.SupplierInvoice;

    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<SupplierInvoiceSummaryDto>> Search([FromQuery] SupplierInvoiceSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines with each line's price variance. 404 Common-E-006.</summary>
    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<SupplierInvoiceDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>Receipt lines of the vendor (and currency) with open qty above 0, without the lines of excludeInvoiceId (MM-BR-763).</summary>
    [HttpGet(SupplierInvoiceRoutes.OpenReceiptLines), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<OpenReceiptLineDto>> OpenReceiptLines(
        [FromQuery] long vendorId, [FromQuery] string? currencyCode, [FromQuery] long? excludeInvoiceId, CancellationToken cancellationToken) =>
        service.OpenReceiptLinesAsync(vendorId, currencyCode, excludeInvoiceId, cancellationToken);

    /// <summary>
    /// 201 with the new invoice and its VI number; price variances are posted. 422 MM-E-751, 753, 754, 764, Common-E-005/006,
    /// BC-E-412, BC-E-423, SD-E-907; 422 MM-W-754 with confirmField confirmPriceDifference (MM-BR-754).
    /// </summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<SupplierInvoiceDto>> Create(SaveSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved invoice. 422 MM-E-760 when paid or a saved line changed, MM-E-762 for a Debit Memo; as on POST.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<SupplierInvoiceDto> Update(long id, SaveSupplierInvoiceRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>204; invoiced qty and price variances are taken back. 422 MM-E-760 when paid, MM-E-762 for a Debit Memo.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }
}
