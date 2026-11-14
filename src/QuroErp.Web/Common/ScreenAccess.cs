using Microsoft.AspNetCore.Components.Authorization;

namespace QuroErp.Web.Common;

/// <summary>
/// Write access of a screen: whether its new, save, delete and action buttons are enabled (security.md "화면 쓰기 권한").
/// Screens ask this service and never check roles themselves.
/// </summary>
public interface IScreenAccess
{
    /// <summary>The signed-in user can write on the screen with this path, e.g. <c>/bc/item</c>.</summary>
    Task<bool> CanWriteAsync(string screenPath);
}

/// <summary>
/// For now the admin role can write on every screen. The target rule is BC-BR-214 (Full Access on a menu of the screen path),
/// which replaces only this class (TASK-CORE-077). The API already checks BC-BR-214, so this decides only the buttons.
/// </summary>
public sealed class ScreenAccess(AuthenticationStateProvider authentication) : IScreenAccess
{
    private const string AdminRole = "admin";

    public async Task<bool> CanWriteAsync(string screenPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenPath);
        // The role claim of each identity, as ClaimsPrincipal's role check reads it
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        return user.Identities.Any(identity => identity.HasClaim(identity.RoleClaimType, AdminRole));
    }
}
