using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuroErp.Application.Common.Auth;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Api.Common.Auth;

[ApiController, Route(AuthRoutes.Base)]
public sealed class AuthController(LoginService service) : ControllerBase
{
    /// <summary>로그인. 인증 없이 부를 수 있는 유일한 API다.</summary>
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(AuthenticationSetup.LoginRateLimitPolicy)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddr = HttpContext.Connection.RemoteIpAddress?.ToString();
        var response = await service.LoginAsync(request, ipAddr, cancellationToken);
        return response is null ? LoginFailed() : response;
    }

    /// <summary>토큰 재발급. Web이 가진 사용자 토큰(JWT)으로만 부른다 (API 키 불가).</summary>
    [HttpPost("refresh"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken cancellationToken)
    {
        var response = await service.RefreshAsync(User.GetAppUserId(), cancellationToken);
        return response is null ? LoginFailed() : response;
    }

    private ObjectResult LoginFailed()
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status401Unauthorized);
        problem.Extensions["code"] = AuthRoutes.LoginFailedCode;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
