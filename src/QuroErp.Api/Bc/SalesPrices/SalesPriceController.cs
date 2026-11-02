using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.SalesPrices;
using QuroErp.Contracts.Bc.SalesPrices;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Bc.SalesPrices;

/// <summary>
/// Sales prices by price level (BC-UC-014). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access
/// (BC-BR-214), checked by the admin role until the menu policy is applied, as on the other screens.
/// </summary>
[ApiController, Route(SalesPriceRoutes.Base)]
public sealed class SalesPriceController(SalesPriceService service) : ControllerBase
{
    /// <summary>The price level's rows in item code order with the item standard price (SD-BR-409). 422 Common-E-005, SD-E-403.</summary>
    [HttpGet]
    public Task<IReadOnlyList<SalesPriceDto>> Search([FromQuery] string? priceLevel, CancellationToken cancellationToken) =>
        service.SearchAsync(priceLevel, cancellationToken);

    /// <summary>
    /// 200 with the level's rows after the save. Nothing is saved when a row fails (SD-BR-409): 422 SD-E-403, SD-E-407, SD-E-408 or BC-E-423 with the
    /// row's item code, 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPost(SalesPriceRoutes.Batch), Authorize(Roles = "admin")]
    public Task<IReadOnlyList<SalesPriceDto>> Save(
        [FromQuery] string? priceLevel, BatchRequest<SalesPriceRowRequest, SalesPriceDeleteRequest> request,
        CancellationToken cancellationToken) =>
        service.SaveAsync(priceLevel, request, User.Identity!.Name!, cancellationToken);
}
