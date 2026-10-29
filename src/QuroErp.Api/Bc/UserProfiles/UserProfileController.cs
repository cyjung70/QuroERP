using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Contracts.Bc.UserProfiles;

namespace QuroErp.Api.Bc.UserProfiles;

/// <summary>
/// My info (BC-UC-003). The user comes from the token, never from the route or body (BC-BR-101).
/// There is no POST or DELETE on the base route, so those answer 405 (BC-BR-104).
/// </summary>
[ApiController, Route(UserProfileRoutes.Base), Authorize(Policy = AuthenticationSetup.UserPolicy)]
public sealed class UserProfileController(UserProfileService service) : ControllerBase
{
    [HttpGet]
    public Task<UserProfileDto> Get(CancellationToken cancellationToken) => service.GetAsync(User.GetAppUserId(), cancellationToken);

    /// <summary>200 with the saved info. 422 BC-E-102 or BC-E-403; 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpPut]
    public Task<UserProfileDto> Update(UpdateUserProfileRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(User.GetAppUserId(), request, User.Identity!.Name!, cancellationToken);

    /// <summary>204. Revokes all my auto-login tokens; this session stays (BC-BR-106).</summary>
    [HttpPost("revoke-tokens")]
    public async Task<IActionResult> RevokeTokens(CancellationToken cancellationToken)
    {
        await service.RevokeTokensAsync(User.GetAppUserId(), cancellationToken);
        return NoContent();
    }
}
