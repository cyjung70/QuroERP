using QuroErp.Contracts.Bc.Roles;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Roles;

/// <summary>Role API client (GET api/sys/roles).</summary>
public sealed class RoleApiClient(HttpClient http, AccessTokenProvider tokens)
{
    /// <summary>Roles in use, in sort order, for select fields.</summary>
    public async Task<IReadOnlyList<RoleDto>> GetInUseAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, RoleRoutes.Base), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<RoleDto>>(cancellationToken)
            ?? throw new InvalidOperationException("Role API returned an empty body.");
    }
}
