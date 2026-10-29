using System.Collections.Concurrent;
using QuroErp.Contracts.Common.Auth;

namespace QuroErp.Web.Auth;

/// <summary>
/// 로그인 세션별 내부 토큰을 Web 서버 메모리에 둔다 (security.md: 토큰은 브라우저에 보내지 않는다).
/// 쿠키에는 세션 ID만 들어간다. Web을 다시 시작하면 모든 세션이 끝난다.
/// The Web host runs as a single instance (login.md "세션"); more instances need a shared store (distributed cache) first.
/// A session also ends after its idle timeout (session_idle_minutes) without activity (<see cref="Touch"/>).
/// </summary>
public sealed class AccessTokenStore(TimeProvider clock)
{
    /// <summary>login.md "설정" default of session_idle_minutes, used when the setting cannot be read.</summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    /// <returns>새 세션 ID.</returns>
    public string Add(LoginResponse login, TimeSpan? idleTimeout = null)
    {
        RemoveExpired();
        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = new Session(login, idleTimeout is { } t && t > TimeSpan.Zero ? t : DefaultIdleTimeout, clock.GetUtcNow());
        return sessionId;
    }

    /// <summary>The session's login, without counting as activity. Null when it ended.</summary>
    public LoginResponse? Get(string sessionId) => Find(sessionId, out _);

    /// <param name="idleExpired">True when the session ended now because of the idle timeout (AUTH-006).</param>
    public LoginResponse? Find(string sessionId, out bool idleExpired)
    {
        idleExpired = false;
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }
        var now = clock.GetUtcNow();
        if (session.Login.ExpiresAt <= now || session.IdleExpiresAt <= now)
        {
            idleExpired = session.Login.ExpiresAt > now;
            _sessions.TryRemove(sessionId, out _);
            return null;
        }
        return session.Login;
    }

    /// <summary>When the session ends without further activity. Null when it already ended.</summary>
    public DateTimeOffset? IdleExpiresAt(string sessionId) =>
        Get(sessionId) is not null && _sessions.TryGetValue(sessionId, out var session) ? session.IdleExpiresAt : null;

    /// <summary>A request of the user: the idle timeout starts again (login.md "세션": 요청마다 연장).</summary>
    public void Touch(string sessionId)
    {
        if (Get(sessionId) is not null && _sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with { LastActiveAt = clock.GetUtcNow() };
        }
    }

    public void Replace(string sessionId, LoginResponse login)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions[sessionId] = session with { Login = login };
        }
    }

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    private void RemoveExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var (sessionId, session) in _sessions)
        {
            if (session.Login.ExpiresAt <= now || session.IdleExpiresAt <= now)
            {
                _sessions.TryRemove(sessionId, out _);
            }
        }
    }

    private sealed record Session(LoginResponse Login, TimeSpan IdleTimeout, DateTimeOffset LastActiveAt)
    {
        public DateTimeOffset IdleExpiresAt => LastActiveAt + IdleTimeout;
    }
}
