using System.Collections.Concurrent;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Web.Auth;

/// <summary>
/// 로그인 세션별 내부 토큰을 Web 서버 메모리에 둔다 (security.md: 토큰은 브라우저에 보내지 않는다).
/// 쿠키에는 세션 ID만 들어간다. Web을 다시 시작하면 모든 세션이 끝난다.
/// [확인 필요] Web을 여러 대로 늘리면 공유 저장소(분산 캐시)로 바꾼다.
/// </summary>
public sealed class AccessTokenStore(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, LoginResponse> _sessions = new(StringComparer.Ordinal);

    /// <returns>새 세션 ID.</returns>
    public string Add(LoginResponse login)
    {
        RemoveExpired();
        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = login;
        return sessionId;
    }

    public LoginResponse? Get(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var login))
        {
            return null;
        }
        if (login.ExpiresAt <= clock.GetUtcNow())
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }
        return login;
    }

    public void Replace(string sessionId, LoginResponse login) => _sessions[sessionId] = login;

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    private void RemoveExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var (sessionId, login) in _sessions)
        {
            if (login.ExpiresAt <= now)
            {
                _sessions.TryRemove(sessionId, out _);
            }
        }
    }
}
