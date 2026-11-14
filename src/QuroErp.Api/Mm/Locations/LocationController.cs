using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Mm.Locations;
using QuroErp.Contracts.Common;
using QuroErp.Contracts.Mm.Locations;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Mm.Locations;

/// <summary>
/// Location (shelf) master per warehouse (MM-UC-034) and the Location choice list. Any signed-in user or API client can read
/// (FallbackPolicy); changes need Full Access on the screen (BC-BR-214), checked by the screen path policy.
/// </summary>
[ApiController]
public sealed class LocationController(LocationService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Location;

    /// <summary>The Locations of one warehouse, '*' first. 422 Common-E-005 without warehouseId.</summary>
    [HttpGet(LocationRoutes.Base)]
    public Task<IReadOnlyList<LocationDto>> Search([FromQuery] LocationSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    /// <summary>Locations in use of the warehouse, '*' first (MM-BR-717).</summary>
    [HttpGet(LocationRoutes.Options)]
    public Task<IReadOnlyList<LocationOptionDto>> GetOptions([FromQuery] long warehouseId, CancellationToken cancellationToken) =>
        service.GetOptionsAsync(warehouseId, cancellationToken);

    /// <summary>
    /// 200 with the Locations of the warehouses in the save. Nothing is saved when a row fails: 422 MM-E-712 (code not valid,
    /// repeated in the warehouse or changed; the system Location '*' changed or deleted), MM-E-717 with the code and ONHAND or
    /// HAS_MOVEMENT, Common-E-005 (no or unknown warehouse on a new row); 409 Common-E-004 when a row changed after it was read.
    /// </summary>
    [HttpPost(LocationRoutes.Batch), Authorize(Policy = Screen)]
    public Task<IReadOnlyList<LocationDto>> Save(BatchRequest<LocationSaveRow, LocationKey> request, CancellationToken cancellationToken) =>
        service.SaveAsync(request, User.Identity!.Name!, cancellationToken);
}
