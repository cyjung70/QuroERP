using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

/// <summary>Web → Api 호출에 쓰는 내부 토큰을 발급한다. 구현은 Api 호스트에 있다.</summary>
public interface IAccessTokenIssuer
{
    /// <param name="mustChangePassword">Marks the token so other APIs return 403 until the password is changed.</param>
    AccessToken Issue(AppUser user, bool mustChangePassword, DateTimeOffset now);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
