using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using QuroErp.Application.Bc.Roles;

namespace QuroErp.Api.Common.Auth;

/// <summary>
/// The "Menu:&lt;menu_code&gt;" API policy (security.md "권한", BC-BR-214): GET and HEAD need the menu assigned to the caller's role,
/// every other method needs Full Access. Menus not in use and roles not in use give no access (BC-BR-206).
/// Controllers still use the admin role until the menu data is loaded; they switch with <c>[Authorize(Policy = MenuPolicy.Prefix + "CODE")]</c>.
/// </summary>
public static class MenuPolicy
{
    public const string Prefix = "Menu:";
}

public sealed record MenuRequirement(string MenuCode) : IAuthorizationRequirement;

/// <summary>Builds a policy for any "Menu:&lt;code&gt;" name; other names go to the registered policies.</summary>
public sealed class MenuPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(MenuPolicy.Prefix, StringComparison.Ordinal) || policyName.Length == MenuPolicy.Prefix.Length)
        {
            return await base.GetPolicyAsync(policyName);
        }
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangedRequirement(), new MenuRequirement(policyName[MenuPolicy.Prefix.Length..]))
            .Build();
    }
}

public sealed class MenuAuthorizationHandler(MenuPermissionService permissions) : AuthorizationHandler<MenuRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, MenuRequirement requirement)
    {
        // A JWT user and an API key caller both carry the role claim (security.md "API 키 호출자는 API 키에 붙은 역할로 검사")
        if (context.User.FindFirst(AuthClaims.Role)?.Value is not { Length: > 0 } roleCode)
        {
            return;
        }
        var http = context.Resource as HttpContext;
        // Without a request the method is unknown, so it needs Full Access
        var readOnly = http is not null && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));
        var access = await permissions.GetAccessAsync(roleCode, requirement.MenuCode, http?.RequestAborted ?? CancellationToken.None);
        if (access == MenuAccess.Full || (readOnly && access == MenuAccess.Read))
        {
            context.Succeed(requirement);
        }
    }
}
