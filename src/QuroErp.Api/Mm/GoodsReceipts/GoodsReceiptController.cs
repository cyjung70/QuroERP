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
public sealed class GoodsReceiptController(GoodsReceiptService service, GoodsReceiptLabelService labels) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.GoodsReceipt;

    /// <summary>Newest first, at most 200 rows.</summary>
    [HttpGet, Authorize(Policy = Screen)]
    public Task<IReadOnlyList<GoodsReceiptSummaryDto>> Search([FromQuery] GoodsReceiptSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Header and lines. 404 Common-E-006.</summary>
    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<GoodsReceiptDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>
    /// The labels of the receipt's ACTIVE Pallets as a PDF: each Pallet label, then its DataMatrix label (MM-BR-918). Each print
    /// is kept in the label history (MM-BR-919). 404 Common-E-006 when the receipt has no ACTIVE Pallet.
    /// </summary>
    [HttpGet("{id:long}/" + GoodsReceiptRoutes.PalletLabels), Authorize(Policy = Screen)]
    public async Task<IActionResult> PalletLabels(long id, CancellationToken cancellationToken)
    {
        var output = await labels.PrintPalletLabelsAsync(id, User.Identity!.Name!, cancellationToken);
        return File(output.Content, output.ContentType);
    }

    /// <summary>
    /// PO lines of PO and RE orders with remaining qty above 0 (MM-BR-113): of the vendor, or of the order with poNo for the PDA
    /// (MM-BR-920). itemId keeps the order's lines of a scanned item. 422 Common-E-005 without vendorId or poNo; MM-E-923 for an
    /// unknown PO number or an item with no remaining line on it (MM-BR-923); MM-E-024 for an order that cannot be received.
    /// </summary>
    [HttpGet(GoodsReceiptRoutes.OpenPoLines), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<OpenPoLineDto>> OpenPoLines(
        [FromQuery] long? vendorId, [FromQuery] string? poNo, [FromQuery] long? itemId, CancellationToken cancellationToken) =>
        service.OpenPoLinesAsync(vendorId, poNo, itemId, cancellationToken);

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

    /// <summary>
    /// 200 with the saved receipt. 422 MM-E-104 when an invoiced or returned receipt changes more than Lot attributes, MM-E-107,
    /// 116, 117, 364, 910, 911; MM-W-102 as on POST.
    /// </summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<GoodsReceiptDto> Update(long id, SaveGoodsReceiptRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);

    /// <summary>
    /// 204; the stock and PO received qty are taken back and the Pallets closed. 422 MM-E-104 when a line is invoiced or returned,
    /// MM-E-911 when a Pallet was moved, shipped, split, merged or adjusted.
    /// </summary>
    [HttpDelete("{id:long}"), Authorize(Policy = Screen)]
    public async Task<IActionResult> Delete(long id, [FromQuery] DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, updatedAt, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }
}
