using System.Globalization;
using System.Net;
using QuroErp.Contracts.Bc.Users;
using QuroErp.Web.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Bc.Users;

/// <summary>User management API client (api/bc/users, BC-UC-004).</summary>
public sealed class UserApiClient(HttpClient http, AccessTokenProvider tokens)
{
    public async Task<UserListDto> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"size={query.Size.ToString(CultureInfo.InvariantCulture)}",
        };
        Add(parameters, "userId", query.UserId);
        Add(parameters, "userName", query.UserName);
        Add(parameters, "validFlag", query.ValidFlag);
        if (query.RoleId is { } roleId)
        {
            parameters.Add($"roleId={roleId.ToString(CultureInfo.InvariantCulture)}");
        }

        using var response = await http.SendAuthorizedAsync(
            tokens, new HttpRequestMessage(HttpMethod.Get, $"{UserRoutes.Base}?{string.Join('&', parameters)}"), cancellationToken);
        return await response.Content.ReadFromJsonAsync<UserListDto>(cancellationToken)
            ?? throw new InvalidOperationException("User API returned an empty body.");
    }

    /// <summary>The user with exactly this user ID, or null. User IDs are stored in lower case (BC-BR-121).</summary>
    /// <remarks>The list matches by prefix, so the exact ID is picked from the first page of matches.</remarks>
    public async Task<UserDto?> FindByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        var typed = userId.Trim();
        var list = await SearchAsync(new UserSearchQuery { UserId = typed, Size = UserRoutes.MaxPageSize }, cancellationToken);
        return list.Items.FirstOrDefault(u => string.Equals(u.UserId, typed, StringComparison.OrdinalIgnoreCase));
    }

    /// <exception cref="ApiProblemException">404 Common-E-006 when the user is gone.</exception>
    public async Task<UserDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, UserUri(id)), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>The user before (forward = false) or after this one in user ID order; null at either end.</summary>
    public async Task<UserDto?> GetAdjacentAsync(long id, bool forward, CancellationToken cancellationToken)
    {
        var uri = $"{UserUri(id)}/{(forward ? UserRoutes.Next : UserRoutes.Previous)}";
        using var response = await http.SendAuthorizedAsync(tokens, new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        return response.StatusCode == HttpStatusCode.NoContent ? null : await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 BC-E-121, BC-E-122, BC-E-405 or Common-E-002; 400 AUTH-001 or AUTH-011~014.</exception>
    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, UserRoutes.Base) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <exception cref="ApiProblemException">422 BC-E-122, BC-E-131, BC-E-405 or Common-E-002; 409 Common-E-004.</exception>
    public async Task<UserDto> UpdateAsync(long id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, UserUri(id)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>Clears the failure count and the lock (BC-BR-128).</summary>
    public Task UnlockAsync(long id, CancellationToken cancellationToken) => PostActionAsync(id, UserRoutes.Unlock, null, cancellationToken);

    /// <exception cref="ApiProblemException">400 AUTH-001 or AUTH-011~015 when the new password breaks the rules (BC-BR-129).</exception>
    public Task ResetPasswordAsync(long id, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        PostActionAsync(id, UserRoutes.ResetPassword, JsonContent.Create(request), cancellationToken);

    /// <summary>Revokes the user's auto-login tokens and raises the token version (BC-BR-130).</summary>
    public Task RevokeTokensAsync(long id, CancellationToken cancellationToken) =>
        PostActionAsync(id, UserRoutes.RevokeTokens, null, cancellationToken);

    private async Task PostActionAsync(long id, string action, HttpContent? content, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{UserUri(id)}/{action}") { Content = content };
        using var response = await http.SendAuthorizedAsync(tokens, message, cancellationToken);
    }

    private static string UserUri(long id) => $"{UserRoutes.Base}/{id.ToString(CultureInfo.InvariantCulture)}";

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    private static async Task<UserDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<UserDto>(cancellationToken)
            ?? throw new InvalidOperationException("User API returned an empty body.");
}
