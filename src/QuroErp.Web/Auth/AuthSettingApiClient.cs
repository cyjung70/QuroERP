using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Auth;

/// <summary>Security settings API client (api/sys/auth-settings, BC-UC-007).</summary>
public sealed class AuthSettingApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public async Task<AuthSettingDto> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, AuthSettingRoutes.Base), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 BC-E-309 naming the property, 409 Common-E-004, 403 for non-admin users.</exception>
    public async Task<AuthSettingDto> UpdateAsync(AuthSettingDto request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, AuthSettingRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<AuthSettingDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<AuthSettingDto>(cancellationToken)
        ?? throw new InvalidOperationException("Security setting API returned an empty body.");
}
