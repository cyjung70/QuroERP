using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common.Auth;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Api.Mm.Labels;

/// <summary>
/// Barcode label printing (MM-UC-037). Any signed-in user or API client can read the targets and the history (FallbackPolicy);
/// a print needs Full Access on the screen (BC-BR-214), checked by the screen path policy.
/// </summary>
[ApiController]
public sealed class LabelPrintController(LabelPrintRequestService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.LabelPrint;

    /// <summary>
    /// What can be printed, in code order, at most 1000 rows. 422 Common-E-005 without a label type or, for LOCATION, a warehouse;
    /// Common-E-002 for an unknown label type.
    /// </summary>
    [HttpGet(LabelPrintRoutes.Targets)]
    public Task<IReadOnlyList<LabelTargetDto>> Targets([FromQuery] LabelTargetQuery query, CancellationToken cancellationToken) =>
        service.TargetsAsync(query, cancellationToken);

    /// <summary>
    /// Prints the targets in order, each its own copies; a Pallet copy is its label, then its DataMatrix label (MM-BR-918). Each
    /// target is kept in the history (MM-BR-919). ZPL answers JSON { outputKind, zpl } for the client to send to the printer; PDF
    /// answers application/pdf. 404 Common-E-006 when the format is not in use or a target does not exist; 422 Common-E-005 or
    /// Common-E-002 for a missing or unknown value.
    /// </summary>
    [HttpPost(LabelPrintRoutes.Base), Authorize(Policy = Screen)]
    public async Task<IActionResult> Print(LabelPrintRequest request, CancellationToken cancellationToken)
    {
        var output = await service.PrintAsync(request, User.Identity!.Name!, cancellationToken);
        return output.OutputKind == LabelType.Zpl
            ? Ok(new LabelPrintZplDto(output.OutputKind, System.Text.Encoding.UTF8.GetString(output.Content)))
            : File(output.Content, output.ContentType);
    }

    /// <summary>Prints on the company dates from..to, of the label type when given, newest first, at most 1000 rows.</summary>
    [HttpGet(LabelPrintRoutes.History)]
    public Task<IReadOnlyList<LabelPrintHistoryDto>> History([FromQuery] LabelPrintHistoryQuery query, CancellationToken cancellationToken) =>
        service.HistoryAsync(query, cancellationToken);
}
