using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Common.Auth;

/// <summary>
/// Authentication API (BC-UC-001, login.md). Every endpoint is rate limited per IP.
/// Failures are Problem Details with code AUTH-xxx (<see cref="AuthProblems"/>).
/// </summary>
[ApiController, Route(AuthRoutes.Base), EnableRateLimiting(AuthenticationSetup.AuthRateLimitPolicy)]
public sealed class AuthController(LoginService service) : ControllerBase
{
    /// <summary>로그인. 인증 없이 부를 수 있는 API다.</summary>
    [HttpPost("login"), AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        AuthProblems.UseLanguage(HttpContext, request.Language);
        return await service.LoginAsync(request, Client(), cancellationToken);
    }

    /// <summary>
    /// Signs in with the auto-login token and returns a replacement token. Anonymous by nature:
    /// it is called when the session is gone (login.md "자동 로그인").
    /// </summary>
    [HttpPost("auto-login"), AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> AutoLogin(AutoLoginRequest request, CancellationToken cancellationToken) =>
        await service.AutoLoginAsync(request, Client(), cancellationToken);

    /// <summary>
    /// Revokes this device's auto-login token and logs LOGOUT. Anonymous too: after the session is gone the token alone
    /// revokes itself (login.md "로그아웃"). Always 204, so it never tells whether a token exists.
    /// </summary>
    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        // Only a user token (JWT) counts as a session here, never an API key
        var session = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        await service.LogoutAsync(session.Succeeded ? session.Principal.GetAppUserId() : null, request, Client(), cancellationToken);
        return NoContent();
    }

    /// <summary>Login screen settings. Anonymous: the screen reads them before sign-in (login.md).</summary>
    [HttpGet("login-options"), AllowAnonymous]
    public Task<LoginOptionsResponse> LoginOptions(CancellationToken cancellationToken) =>
        service.GetLoginOptionsAsync(cancellationToken);

    /// <summary>User, permissions, menus and default warehouse. Allowed before a forced password change.</summary>
    [HttpGet("me"), Authorize(Policy = AuthenticationSetup.SessionPolicy)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken) =>
        await service.MeAsync(User.GetAppUserId(), cancellationToken);

    /// <summary>토큰 재발급. Web이 가진 사용자 토큰(JWT)으로만 부른다 (API 키 불가).</summary>
    [HttpPost("refresh"), Authorize(Policy = AuthenticationSetup.SessionPolicy)]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken cancellationToken) =>
        await service.RefreshAsync(User.GetAppUserId(), User.GetTokenVersion(), cancellationToken)
            ?? throw new AuthException(AuthCodes.SessionExpired);

    /// <summary>
    /// Changes the signed-in user's password (BC-UC-002). Allowed before a forced password change. 200 with a new token
    /// for this session (BC-BR-058); 400 AUTH-001, AUTH-011~015.
    /// </summary>
    [HttpPost("change-password"), Authorize(Policy = AuthenticationSetup.SessionPolicy)]
    public async Task<ActionResult<LoginResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        await service.ChangePasswordAsync(User.GetAppUserId(), request, cancellationToken);

    private AuthClient Client() =>
        AuthClient.Create(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}
