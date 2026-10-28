using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Bc.Localization;
using QuroErp.Contracts.Bc.Localization;

namespace QuroErp.Api.Bc.Localization;

/// <summary>
/// The localization setting screen (BC-UC-027). Any signed-in user can read it; changes need Full Access (BC-BR-427),
/// checked by the admin role until the menu policy (security.md "권한") exists, as on the company screen.
/// </summary>
[ApiController, Route(LocalizationRoutes.Setting)]
public sealed class LocalizationSettingController(LocalizationSettingService service) : ControllerBase
{
    [HttpGet]
    public Task<LocalizationSettingDto> Get(CancellationToken cancellationToken) => service.GetAsync(cancellationToken);

    /// <summary>200 with the saved setting. 409 BC-E-425 when decimals that documents use are reduced, 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpPut, Authorize(Roles = "admin")]
    public Task<LocalizationSettingDto> Update(UpdateLocalizationSettingRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(request, User.Identity!.Name!, cancellationToken);
}
