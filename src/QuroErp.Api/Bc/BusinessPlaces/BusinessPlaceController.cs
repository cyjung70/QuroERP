using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.BusinessPlaces;
using QuroErp.Contracts.Bc.BusinessPlaces;

namespace QuroErp.Api.Bc.BusinessPlaces;

/// <summary>
/// Business places on the company screen (BC-BR-319, BC-BR-320). Any signed-in user can read them; changes need Full Access on the
/// company menu (BC-BR-305), checked by the admin role until the menu policy is applied. There is no POST or DELETE in the MVP (405).
/// </summary>
[ApiController]
public sealed class BusinessPlaceController(BusinessPlaceService places) : ControllerBase
{
    [HttpGet(BusinessPlaceRoutes.Base)]
    public Task<IReadOnlyList<BusinessPlaceDto>> List(CancellationToken cancellationToken) => places.ListAsync(cancellationToken);

    /// <summary>200 with the saved place. 422 BC-E-320 with the property name or BC-E-319, 404 Common-E-006, 409 Common-E-004.</summary>
    [HttpPut(BusinessPlaceRoutes.ById), Authorize(Roles = "admin")]
    public Task<BusinessPlaceDto> Update(long id, UpdateBusinessPlaceRequest request, CancellationToken cancellationToken) =>
        places.UpdateAsync(id, request, User.Identity!.Name!, cancellationToken);
}
