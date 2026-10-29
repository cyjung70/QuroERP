using FluentAssertions;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Web.Auth;

namespace QuroErp.Web.Tests.Auth;

/// <summary>Idle timeout of the Web sessions (login.md "세션", TASK-BC-002 part 3).</summary>
public class AccessTokenStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private readonly TestClock _clock = new(Start);
    private readonly AccessTokenStore _store;

    public AccessTokenStoreTests() => _store = new AccessTokenStore(_clock);

    [Fact, Trait("Id", "BC-002-UT-04")]
    public void Find_AfterTheIdleTimeout_EndsTheSessionAsIdle()
    {
        var sessionId = _store.Add(Login(), TimeSpan.FromMinutes(30));

        _clock.Advance(TimeSpan.FromMinutes(29));
        _store.Get(sessionId).Should().NotBeNull();
        _clock.Advance(TimeSpan.FromMinutes(1));

        _store.Find(sessionId, out var idleExpired).Should().BeNull();
        idleExpired.Should().BeTrue();
        _store.Find(sessionId, out idleExpired).Should().BeNull();
        idleExpired.Should().BeFalse();
    }

    [Fact, Trait("Id", "BC-002-UT-05")]
    public void Touch_StartsTheIdleTimeoutAgain_GetDoesNot()
    {
        var sessionId = _store.Add(Login(), TimeSpan.FromMinutes(30));

        _clock.Advance(TimeSpan.FromMinutes(20));
        _store.Get(sessionId);
        _store.Touch(sessionId);
        _clock.Advance(TimeSpan.FromMinutes(20));

        _store.IdleExpiresAt(sessionId).Should().Be(Start.AddMinutes(50));
    }

    [Fact, Trait("Id", "BC-002-UT-06")]
    public void Find_TokenExpired_IsNotAnIdleEnd()
    {
        var sessionId = _store.Add(Login(expiresAt: Start.AddMinutes(10)), TimeSpan.FromMinutes(30));
        _clock.Advance(TimeSpan.FromMinutes(10));

        _store.Find(sessionId, out var idleExpired).Should().BeNull();
        idleExpired.Should().BeFalse();
    }

    [Theory, Trait("Id", "BC-002-UT-07")]
    [InlineData(null)]
    [InlineData(0)]
    public void Add_NoUsableIdleTimeout_UsesTheDefault30Minutes(int? minutes)
    {
        var sessionId = _store.Add(Login(), minutes is { } m ? TimeSpan.FromMinutes(m) : null);

        _store.IdleExpiresAt(sessionId).Should().Be(Start + AccessTokenStore.DefaultIdleTimeout);
    }

    [Fact, Trait("Id", "BC-002-UT-08")]
    public void Replace_KeepsTheIdleTimeout()
    {
        var sessionId = _store.Add(Login(), TimeSpan.FromMinutes(10));

        _store.Replace(sessionId, Login("token-new"));

        _store.Get(sessionId)!.AccessToken.Should().Be("token-new");
        _store.IdleExpiresAt(sessionId).Should().Be(Start.AddMinutes(10));
    }

    private static LoginResponse Login(string token = "token", DateTimeOffset? expiresAt = null) =>
        new(token, expiresAt ?? Start.AddHours(24), "kim", "Kim", "user", false, "ko");
}
