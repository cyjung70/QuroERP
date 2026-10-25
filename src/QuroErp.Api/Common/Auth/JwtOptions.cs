namespace QuroErp.Api.Common.Auth;

/// <summary>내부 토큰(JWT, HS256) 설정. 설정 절 "Jwt". SigningKey는 user-secrets·환경변수로만 둔다.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 키의 최소 길이 (바이트).</summary>
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "QuroErp.Api";

    public string Audience { get; set; } = "QuroErp.Web";

    public string SigningKey { get; set; } = "";

    /// <summary>토큰 만료 시간. 기본 24시간 (TASK-CORE-003 결정). Web 세션이 살아 있으면 재발급한다.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(24);
}
