using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.Items;
using QuroErp.Contracts.Bc.Items;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Bc.Items;

/// <summary>
/// /api/bc/item-barcodes (BC-UC-011). Any signed-in user reads; generation needs Full Access on the item barcode screen,
/// checked by the screen path policy (security.md "권한").
/// </summary>
[ApiController, Route(ItemBarcodeRoutes.Base)]
public sealed class ItemBarcodeController(ItemBarcodeService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.ItemBarcode;

    /// <summary>200 with the items of the item class in item code order. 422 MM-E-802 without an item class.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ItemBarcodeDto>> List([FromQuery] ItemBarcodeQuery query, CancellationToken cancellationToken) =>
        service.ListAsync(query, cancellationToken);

    /// <summary>200 with the counts, new barcodes and conflicts of a run; nothing is saved. 422 MM-E-802, MM-E-816.</summary>
    [HttpGet(ItemBarcodeRoutes.Preview)]
    public Task<ItemBarcodePreviewDto> Preview([FromQuery] ItemBarcodePreviewQuery query, CancellationToken cancellationToken) =>
        service.PreviewAsync(query, cancellationToken);

    /// <summary>200 with the number of items changed, all in one transaction (MM-BR-818). 422 MM-E-802, MM-E-816, MM-E-817.</summary>
    [HttpPost(ItemBarcodeRoutes.Generate), Authorize(Policy = Screen)]
    public Task<GenerateItemBarcodesResultDto> Generate(GenerateItemBarcodesRequest request, CancellationToken cancellationToken) =>
        service.GenerateAsync(request, User.Identity!.Name!, cancellationToken);
}
