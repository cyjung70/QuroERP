using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using QuroErp.Application.Bc.Roles;

namespace QuroErp.Api.Common.Auth;

/// <summary>
/// The "Screen:&lt;screen path&gt;" API policy (security.md "권한", BC-BR-214): GET and HEAD need a menu with that program id
/// assigned to the caller's role, every other method needs Full Access on one. Menus not in use and roles not in use give no
/// access (BC-BR-206). Controllers use <c>[Authorize(Policy = ScreenPolicy.Prefix + ScreenPaths.X)]</c>.
/// </summary>
public static class ScreenPolicy
{
    public const string Prefix = "Screen:";
}

public sealed record ScreenRequirement(string ScreenPath) : IAuthorizationRequirement;

/// <summary>Builds a policy for any "Screen:&lt;path&gt;" name; other names go to the registered policies.</summary>
public sealed class ScreenPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(ScreenPolicy.Prefix, StringComparison.Ordinal) || policyName.Length == ScreenPolicy.Prefix.Length)
        {
            return await base.GetPolicyAsync(policyName);
        }
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangedRequirement(), new ScreenRequirement(policyName[ScreenPolicy.Prefix.Length..]))
            .Build();
    }
}

public sealed class ScreenAuthorizationHandler(MenuPermissionService permissions) : AuthorizationHandler<ScreenRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ScreenRequirement requirement)
    {
        // A JWT user and an API key caller both carry the role claim (security.md "API 키 호출자는 API 키에 붙은 역할로 검사")
        if (context.User.FindFirst(AuthClaims.Role)?.Value is not { Length: > 0 } roleCode)
        {
            return;
        }
        var http = context.Resource as HttpContext;
        // Without a request the method is unknown, so it needs Full Access
        var readOnly = http is not null && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));
        var access = await permissions.GetAccessAsync(roleCode, requirement.ScreenPath, http?.RequestAborted ?? CancellationToken.None);
        if (access == MenuAccess.Full || (readOnly && access == MenuAccess.Read))
        {
            context.Succeed(requirement);
        }
    }
}
