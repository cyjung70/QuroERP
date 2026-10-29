using System.Net;
using System.Net.Http.Headers;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Auth;

/// <summary>인증 API 클라이언트 (POST api/auth/login, auto-login, logout, refresh, change-password).</summary>
public sealed class AuthApiClient(HttpClient http)
{
    /// <param name="clientIp">브라우저 IP. Api가 로그인 이력에 남기도록 X-Forwarded-For로 보낸다.</param>
    /// <param name="userAgent">Browser User-Agent, stored in the login log.</param>
    /// <exception cref="ApiProblemException">Login failed: code AUTH-002 (401), AUTH-003 (423), AUTH-004 (403) or AUTH-008 (429).</exception>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        using var message = ClientMessage(AuthRoutes.Login, request, clientIp, userAgent);
        return await SendAsync(message, cancellationToken);
    }

    /// <summary>Signs in with the token cookie's raw token. The response carries the replacement token.</summary>
    /// <exception cref="ApiProblemException">401 AUTH-007 when the token is expired, revoked or auto login is off.</exception>
    public async Task<LoginResponse> AutoLoginAsync(string token, string? clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        using var message = ClientMessage(AuthRoutes.AutoLogin, new AutoLoginRequest(token), clientIp, userAgent);
        return await SendAsync(message, cancellationToken);
    }

    /// <summary>Revokes this device's auto-login token (if any) and logs LOGOUT.</summary>
    /// <param name="accessToken">This session's token. Null after the session is gone: the Api revokes the request's token alone.</param>
    public async Task LogoutAsync(string? accessToken, LogoutRequest request, string? clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        using var message = ClientMessage(AuthRoutes.Logout, request, clientIp, userAgent);
        if (accessToken is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiProblemException(await ApiProblemReader.ReadAsync(response, cancellationToken));
        }
    }

    /// <summary>Login screen settings, read before sign-in (anonymous).</summary>
    public async Task<LoginOptionsResponse> GetLoginOptionsAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<LoginOptionsResponse>(AuthRoutes.LoginOptions, cancellationToken)
            ?? throw new InvalidOperationException("Login options response body is empty.");

    /// <returns>토큰이 만료됐거나 사용자가 사용 중지됐으면 null.</returns>
    public async Task<LoginResponse?> RefreshAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, AuthRoutes.Refresh);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            return await SendAsync(message, cancellationToken);
        }
        catch (ApiProblemException e) when (e.Problem.Status == (int)HttpStatusCode.Unauthorized)
        {
            return null;
        }
    }

    /// <summary>Changes the signed-in user's password (BC-UC-002).</summary>
    /// <returns>A new token for this session (BC-BR-058). The caller replaces the stored login with it.</returns>
    /// <exception cref="ApiProblemException">400 AUTH-001 or AUTH-011~015, 401 when the session is gone.</exception>
    public async Task<LoginResponse> ChangePasswordAsync(string accessToken, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, AuthRoutes.ChangePassword) { Content = JsonContent.Create(request) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync(message, cancellationToken);
    }

    // The browser's IP and User-Agent go to the Api for the login log
    private static HttpRequestMessage ClientMessage<T>(string path, T body, string? clientIp, string? userAgent)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrEmpty(clientIp))
        {
            message.Headers.Add("X-Forwarded-For", clientIp);
        }
        if (!string.IsNullOrEmpty(userAgent))
        {
            message.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }
        return message;
    }

    private async Task<LoginResponse> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiProblemException(await ApiProblemReader.ReadAsync(response, cancellationToken));
        }
        return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
            ?? throw new InvalidOperationException("인증 API 응답 본문이 비어 있다.");
    }
}
