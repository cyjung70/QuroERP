using System.Net;
using System.Net.Http.Headers;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Common;

namespace QuroErp.Web.Auth;

/// <summary>인증 API 클라이언트 (POST api/auth/login, refresh).</summary>
public sealed class AuthApiClient(HttpClient http)
{
    /// <param name="clientIp">브라우저 IP. Api가 로그인 이력에 남기도록 X-Forwarded-For로 보낸다.</param>
    /// <param name="userAgent">Browser User-Agent, stored in the login log.</param>
    /// <exception cref="ApiProblemException">Login failed: code AUTH-002 (401), AUTH-003 (423), AUTH-004 (403) or AUTH-008 (429).</exception>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, AuthRoutes.Login) { Content = JsonContent.Create(request) };
        if (!string.IsNullOrEmpty(clientIp))
        {
            message.Headers.Add("X-Forwarded-For", clientIp);
        }
        if (!string.IsNullOrEmpty(userAgent))
        {
            message.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }
        return await SendAsync(message, cancellationToken);
    }

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
