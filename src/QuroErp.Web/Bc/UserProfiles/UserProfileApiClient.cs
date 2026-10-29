using QuroErp.Contracts.Bc.UserProfiles;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.UserProfiles;

/// <summary>My info API client (api/bc/user-profile, BC-UC-003). The API takes the user from the token (BC-BR-101).</summary>
public sealed class UserProfileApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public async Task<UserProfileDto> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, UserProfileRoutes.Base), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 BC-E-102 (arg: property name) or BC-E-403, 409 Common-E-004 when someone saved first.</exception>
    public async Task<UserProfileDto> UpdateAsync(UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, UserProfileRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Revokes all my auto-login tokens; this session stays (BC-BR-106).</summary>
    public async Task RevokeTokensAsync(CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Post, UserProfileRoutes.RevokeTokens), cancellationToken);
    }

    private static async Task<UserProfileDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<UserProfileDto>(cancellationToken)
            ?? throw new InvalidOperationException("User profile API returned an empty body.");
}
