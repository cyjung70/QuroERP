using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Common.Auth;

/// <summary>
/// Security settings on the company screen (BC-UC-007, login.md "설정"). Any signed-in user can read them; changes need Full Access
/// on the company screen (BC-BR-305), checked by the screen path policy.
/// </summary>
[ApiController, Route(AuthSettingRoutes.Base)]
public sealed class AuthSettingController(AuthSettingService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.Company;

    [HttpGet]
    public Task<AuthSettingDto> Get(CancellationToken cancellationToken) => service.GetAsync(cancellationToken);

    /// <summary>200 with the saved settings. 422 BC-E-309 with the property name, 409 Common-E-004.</summary>
    [HttpPut, Authorize(Policy = Screen)]
    public Task<AuthSettingDto> Update(AuthSettingDto request, CancellationToken cancellationToken) =>
        service.UpdateAsync(request, User.Identity!.Name!, cancellationToken);
}
