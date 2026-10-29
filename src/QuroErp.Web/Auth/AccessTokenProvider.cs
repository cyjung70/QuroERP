using Microsoft.AspNetCore.Components.Authorization;

namespace QuroErp.Web.Auth;

/// <summary>
/// 현재 로그인 세션의 내부 토큰을 돌려준다. API 클라이언트가 Authorization: Bearer로 붙인다.
/// 만료가 가까우면 Api에서 재발급한다 (TASK-CORE-003: Web 세션이 살아 있으면 재발급).
/// </summary>
public sealed class AccessTokenProvider(
    AuthenticationStateProvider authenticationState, AccessTokenStore store, AuthApiClient authApi, TimeProvider clock)
{
    /// <summary>남은 시간이 이보다 짧으면 재발급한다 (만료 24시간의 절반).</summary>
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromHours(12);

    /// <returns>로그인하지 않았거나 세션이 끝났으면 null.</returns>
    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        var sessionId = state.User.FindFirst(WebAuthClaims.SessionId)?.Value;
        if (sessionId is null || store.Get(sessionId) is not { } login)
        {
            return null;
        }
        // An Api call from an interactive screen is a request of the user too (login.md "세션": 요청마다 연장)
        store.Touch(sessionId);
        if (login.ExpiresAt - clock.GetUtcNow() > RefreshBefore)
        {
            return login.AccessToken;
        }

        var refreshed = await authApi.RefreshAsync(login.AccessToken, cancellationToken);
        if (refreshed is null)
        {
            store.Remove(sessionId);
            return null;
        }
        store.Replace(sessionId, refreshed);
        return refreshed.AccessToken;
    }
}
