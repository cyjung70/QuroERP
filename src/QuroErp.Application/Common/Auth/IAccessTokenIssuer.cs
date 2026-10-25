using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

/// <summary>Web → Api 호출에 쓰는 내부 토큰을 발급한다. 구현은 Api 호스트에 있다.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(AppUser user, DateTimeOffset now);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
