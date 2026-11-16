using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Mm.Lots;
using QuroErp.Application.Mm.PurchaseOrders;
using QuroErp.Contracts.Mm.Lots;

namespace QuroErp.Api.Mm.Lots;

/// <summary>
/// Lot trace (MM-UC-040): forward from a Lot, backward from an issue, and the same rows as PDF or XLSX (MM-BR-366~369). It only
/// reads, so any signed-in user can call it; the screen is opened through its menu (BC-BR-214).
/// </summary>
[ApiController]
public sealed class LotTraceController(LotTraceService service, LotTracePrintService print) : ControllerBase
{
    private const string Forward = LotTraceRoutes.Base + "/" + LotTraceRoutes.Forward;
    private const string Backward = LotTraceRoutes.Base + "/" + LotTraceRoutes.Backward;

    /// <summary>The Lot and where it went, as tree rows. No rows for Lot '*' or an unknown Lot. 422 Common-E-005.</summary>
    [HttpGet(Forward), Authorize]
    public Task<IReadOnlyList<LotTraceRowDto>> TraceForward([FromQuery] LotTraceForwardQuery query, CancellationToken cancellationToken) =>
        service.ForwardAsync(query, cancellationToken);

    /// <summary>The issues and where their Lots came from, as tree rows. 422 Common-E-005, Common-E-002.</summary>
    [HttpGet(Backward), Authorize]
    public Task<IReadOnlyList<LotTraceRowDto>> TraceBackward([FromQuery] LotTraceBackwardQuery query, CancellationToken cancellationToken) =>
        service.BackwardAsync(query, cancellationToken);

    [HttpGet(Forward + "/" + LotTraceRoutes.ExportPdf), Authorize]
    public async Task<IActionResult> ForwardPdf([FromQuery] LotTraceForwardQuery query, CancellationToken cancellationToken) =>
        Download(await print.ForwardAsync(query, pdf: true, CultureInfo.CurrentUICulture, cancellationToken));

    [HttpGet(Forward + "/" + LotTraceRoutes.ExportXlsx), Authorize]
    public async Task<IActionResult> ForwardXlsx([FromQuery] LotTraceForwardQuery query, CancellationToken cancellationToken) =>
        Download(await print.ForwardAsync(query, pdf: false, CultureInfo.CurrentUICulture, cancellationToken));

    [HttpGet(Backward + "/" + LotTraceRoutes.ExportPdf), Authorize]
    public async Task<IActionResult> BackwardPdf([FromQuery] LotTraceBackwardQuery query, CancellationToken cancellationToken) =>
        Download(await print.BackwardAsync(query, pdf: true, CultureInfo.CurrentUICulture, cancellationToken));

    [HttpGet(Backward + "/" + LotTraceRoutes.ExportXlsx), Authorize]
    public async Task<IActionResult> BackwardXlsx([FromQuery] LotTraceBackwardQuery query, CancellationToken cancellationToken) =>
        Download(await print.BackwardAsync(query, pdf: false, CultureInfo.CurrentUICulture, cancellationToken));

    private FileContentResult Download(PrintFile file) => File(file.Content, file.ContentType, file.FileName);
}
