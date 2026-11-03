using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuroErp.Api.Common.Auth;
using QuroErp.Application.Bc.Users;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Bc.Users;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Bc.Users;

/// <summary>
/// User management (BC-UC-004). Reading needs the user screen and changes need Full Access on it, checked by
/// the screen path policy (BC-BR-214).
/// There is no DELETE: users are set not in use instead, so DELETE answers 405 (BC-BR-125).
/// </summary>
[ApiController, Route(UserRoutes.Base), Authorize(Policy = AuthenticationSetup.UserPolicy)]
public sealed class UserController(UserService service) : ControllerBase
{
    private const string Screen = ScreenPolicy.Prefix + ScreenPaths.User;

    [HttpGet, Authorize(Policy = Screen)]
    public Task<UserListDto> Search([FromQuery] UserSearchQuery query, CancellationToken cancellationToken) =>
        service.SearchAsync(query, cancellationToken);

    [HttpGet("{id:long}"), Authorize(Policy = Screen)]
    public Task<UserDto> Get(long id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    /// <summary>200 with the previous user in user ID order; 204 at the first user.</summary>
    [HttpGet("{id:long}/" + UserRoutes.Previous), Authorize(Policy = Screen)]
    public async Task<ActionResult<UserDto>> Previous(long id, CancellationToken cancellationToken) =>
        await service.GetAdjacentAsync(id, forward: false, cancellationToken) is { } user ? user : NoContent();

    /// <summary>200 with the next user in user ID order; 204 at the last user.</summary>
    [HttpGet("{id:long}/" + UserRoutes.Next), Authorize(Policy = Screen)]
    public async Task<ActionResult<UserDto>> Next(long id, CancellationToken cancellationToken) =>
        await service.GetAdjacentAsync(id, forward: true, cancellationToken) is { } user ? user : NoContent();

    /// <summary>201 with the saved user. 422 BC-E-121, BC-E-122, BC-E-405 or Common-E-002; 400 AUTH-001 or AUTH-011~014.</summary>
    [HttpPost, Authorize(Policy = Screen)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var dto = await service.CreateAsync(request, User.Identity!.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>200 with the saved user. 422 BC-E-122, BC-E-131, BC-E-405 or Common-E-002; 409 Common-E-004 when updatedAt is stale.</summary>
    [HttpPut("{id:long}"), Authorize(Policy = Screen)]
    public Task<UserDto> Update(long id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, request, User.GetAppUserId(), User.Identity!.Name!, cancellationToken);

    /// <summary>204, also when the account was not locked (BC-BR-128).</summary>
    [HttpPost("{id:long}/" + UserRoutes.Unlock), Authorize(Policy = Screen)]
    public async Task<IActionResult> Unlock(long id, CancellationToken cancellationToken)
    {
        await service.UnlockAsync(id, AuthClient.Create(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString()),
            cancellationToken);
        return NoContent();
    }

    /// <summary>204. 400 AUTH-001 or AUTH-011~015 when the new password breaks the rules (BC-BR-129).</summary>
    [HttpPost("{id:long}/" + UserRoutes.ResetPassword), Authorize(Policy = Screen)]
    public async Task<IActionResult> ResetPassword(long id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await service.ResetPasswordAsync(id, request, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }

    /// <summary>204 (BC-BR-130).</summary>
    [HttpPost("{id:long}/" + UserRoutes.RevokeTokens), Authorize(Policy = Screen)]
    public async Task<IActionResult> RevokeTokens(long id, CancellationToken cancellationToken)
    {
        await service.RevokeTokensAsync(id, cancellationToken);
        return NoContent();
    }
}
