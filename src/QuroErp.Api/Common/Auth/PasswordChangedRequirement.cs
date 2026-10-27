using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Common.Auth;

/// <summary>
/// A user who must change the password may call only the auth session APIs until it is changed (login.md "로그인 처리" 9).
/// Part of the default and fallback policies; fails when the token carries the must-change-password claim.
/// </summary>
public sealed class PasswordChangedRequirement : AuthorizationHandler<PasswordChangedRequirement>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PasswordChangedRequirement requirement)
    {
        if (!context.User.HasClaim(AuthClaims.MustChangePassword, AuthClaims.True))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Writes 403 AUTH-005 when only <see cref="PasswordChangedRequirement"/> blocked the request.</summary>
public sealed class AuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<PasswordChangedRequirement>().Any() == true)
        {
            await AuthProblems.WriteAsync(context, AuthCodes.PasswordChangeRequired);
            return;
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
