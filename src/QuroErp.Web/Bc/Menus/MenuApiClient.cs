using QuroErp.Contracts.Bc.Menus;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Menus;

/// <summary>Company menu API client (api/sys/menus, BC-UC-006).</summary>
public sealed class MenuApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Menus whose code starts with menuCode (all when blank), in menu code order.</summary>
    public async Task<IReadOnlyList<MenuDto>> SearchAsync(string? menuCode, CancellationToken cancellationToken)
    {
        var uri = MenuRoutes.Base;
        if (!string.IsNullOrWhiteSpace(menuCode))
        {
            uri += $"?menuCode={Uri.EscapeDataString(menuCode.Trim())}";
        }
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return await ReadMenusAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 BC-E-201~205, BC-E-207 or Common-E-002 naming the row's menu code, 409 Common-E-004.</exception>
    public async Task<IReadOnlyList<MenuDto>> SaveAsync(SaveMenusRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, MenuRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadMenusAsync(response, cancellationToken);
    }

    private static async Task<IReadOnlyList<MenuDto>> ReadMenusAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<List<MenuDto>>(cancellationToken)
        ?? throw new InvalidOperationException("Menu API returned an empty body.");
}
