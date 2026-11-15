using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.Labels;

/// <summary>
/// Label formats (MM-UC-036). Any signed-in user or API client can read (FallbackPolicy), so label printing can use the formats;
/// changes need Full Access on the screen (BC-BR-214), checked by the screen path policy.
/// </summary>
[ApiController]
public sealed class LabelFormatController(LabelFormatService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.LabelFormat;

    /// <summary>The four formats: PALLET, ITEM, LOT, LOCATION.</summary>
    [HttpGet(LabelFormatRoutes.Base)]
    public Task<IReadOnlyList<LabelFormatDto>> List(CancellationToken cancellationToken) => service.ListAsync(cancellationToken);

    /// <summary>
    /// 200 with the four formats after the save. Only changed rows are taken. Nothing is saved when a row fails: 422 MM-E-917 (a
    /// row added or deleted, an unknown or repeated label type, an output kind other than ZPL or PDF, a size not above 0, a dpi
    /// other than 203 or 300, no fields or a field the type does not have or names twice); 409 Common-E-004 when a row changed
    /// after it was read.
    /// </summary>
    [HttpPost(LabelFormatRoutes.Batch), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<LabelFormatDto>> Save(
        BatchRequest<LabelFormatSaveRow, LabelFormatKey> request, CancellationToken cancellationToken) =>
        service.SaveAsync(request, User.Identity!.Name!, cancellationToken);

    /// <summary>A sample label as a PDF in the stored format (MM-BR-918). 404 Common-E-006 for an unknown label type.</summary>
    [HttpGet(LabelFormatRoutes.Preview)]
    public async Task<IActionResult> Preview(string labelType, CancellationToken cancellationToken) =>
        File(await service.PreviewAsync(labelType, cancellationToken), LabelPrintService.PdfContentType);
}
