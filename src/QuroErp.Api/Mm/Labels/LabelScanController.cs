using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Mm.Labels;

namespace QuroErp.Api.Mm.Labels;

/// <summary>
/// Reads a scanned value for the PDA screens (MM-BR-918, MM-BR-923). It only reads, so any signed-in user can call it; the screens
/// are opened through their menus (BC-BR-214).
/// </summary>
[ApiController]
public sealed class LabelScanController(LabelScanService service) : ControllerBase
{
    /// <summary>
    /// The kind, its values and the item of PL, IT and LT; a value without a kind found as an item barcode or code comes back as IT.
    /// 422 MM-E-923 when it is not a label of this system, its item is not found, or its kind is not one of accept.
    /// </summary>
    [HttpGet(LabelScanRoutes.Base), Authorize]
    public Task<LabelScanDto> Read([FromQuery] LabelScanQuery query, CancellationToken cancellationToken) =>
        service.ReadAsync(query, cancellationToken);
}
