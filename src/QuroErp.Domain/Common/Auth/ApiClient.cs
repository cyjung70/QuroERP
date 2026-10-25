namespace QuroErp.Domain.Common.Auth;

/// <summary>외부 API 클라이언트 (sys_api_client). 키는 해시만 가진다.</summary>
public sealed class ApiClient
{
    public long Id { get; init; }

    public string ClientCode { get; init; } = "";

    public string RoleCode { get; init; } = "";

    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>폐기 시각이 없거나 아직 오지 않았으면 쓸 수 있다.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null || RevokedAt > now;
}
