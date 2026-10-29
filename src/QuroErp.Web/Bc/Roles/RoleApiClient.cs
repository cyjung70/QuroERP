using System.Globalization;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Roles;

/// <summary>Role API client (api/sys/roles, BC-UC-005).</summary>
public sealed class RoleApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Roles in use, in sort order, for select fields.</summary>
    public async Task<IReadOnlyList<RoleDto>> GetInUseAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, RoleRoutes.Base), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<RoleDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Role API returned an empty body.");
    }

    /// <exception cref="ApiProblemException">400 BC-E-211 when the role is not in use.</exception>
    public async Task<IReadOnlyList<RoleMenuDto>> GetMenusAsync(long roleId, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, MenusUri(roleId)), cancellationToken);
        return await ReadMenusAsync(response, cancellationToken);
    }

    /// <summary>Menus the role does not have yet whose code starts with menuCode (BC-BR-212).</summary>
    public async Task<IReadOnlyList<MenuOptionDto>> GetUnassignedMenusAsync(long roleId, string? menuCode, CancellationToken cancellationToken)
    {
        var uri = $"{RoleUri(roleId)}/{RoleRoutes.UnassignedMenus}";
        if (!string.IsNullOrWhiteSpace(menuCode))
        {
            uri += $"?menuCode={Uri.EscapeDataString(menuCode.Trim())}";
        }
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<MenuOptionDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Role API returned an empty body.");
    }

    /// <exception cref="ApiProblemException">422 BC-E-212 or BC-E-216, 409 Common-E-004, 404 Common-E-006, 400 BC-E-211.</exception>
    public async Task<IReadOnlyList<RoleMenuDto>> SaveMenusAsync(long roleId, SaveRoleMenusRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, MenusUri(roleId)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadMenusAsync(response, cancellationToken);
    }

    private static string RoleUri(long roleId) => $"{RoleRoutes.Base}/{roleId.ToString(CultureInfo.InvariantCulture)}";

    private static string MenusUri(long roleId) => $"{RoleUri(roleId)}/{RoleRoutes.Menus}";

    private static async Task<IReadOnlyList<RoleMenuDto>> ReadMenusAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<RoleMenuDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Role API returned an empty body.");
}
