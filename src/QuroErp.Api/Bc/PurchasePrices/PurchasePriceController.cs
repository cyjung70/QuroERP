using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.PurchasePrices;
using QuroErp.Contracts.Bc.PurchasePrices;
using QuroErp.Contracts.Common;

namespace QuroErp.Api.Bc.PurchasePrices;

/// <summary>
/// Purchase prices by price group (BC-UC-015). Any signed-in user or API client can read (FallbackPolicy); changes need Full Access
/// (BC-BR-214), checked by the admin role until the menu policy is applied, as on the other screens.
/// </summary>
[ApiController, Route(PurchasePriceRoutes.Base)]
public sealed class PurchasePriceController(PurchasePriceService service) : ControllerBase
{
    /// <summary>The price group's rows in item code order with the item standard price (MM-BR-905). 422 Common-E-005, MM-E-901.</summary>
    [HttpGet]
    public Task<IReadOnlyList<PurchasePriceDto>> Search([FromQuery] string? vendorPrice, CancellationToken cancellationToken) =>
        service.SearchAsync(vendorPrice, cancellationToken);

    /// <summary>
    /// 200 with the group's rows after the save. Nothing is saved when a row fails (MM-BR-904): 422 MM-E-901~903 or BC-E-423 with the
    /// row's item code, 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPost(PurchasePriceRoutes.Batch), Authorize(Roles = "admin")]
    public Task<IReadOnlyList<PurchasePriceDto>> Save(
        [FromQuery] string? vendorPrice, BatchRequest<PurchasePriceRowRequest, PurchasePriceDeleteRequest> request,
        CancellationToken cancellationToken) =>
        service.SaveAsync(vendorPrice, request, User.Identity!.Name!, cancellationToken);
}
