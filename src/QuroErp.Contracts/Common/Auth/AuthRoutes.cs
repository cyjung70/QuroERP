namespace QuroErp.Contracts.Common.Auth;

/// <summary>인증 API 경로 (api.md "경로").</summary>
public static class AuthRoutes
{
    public const string Base = "api/common/auth";
    public const string Login = Base + "/login";
    public const string Refresh = Base + "/refresh";

    /// <summary>외부 서버가 쓰는 API 키 헤더 (security.md "인증 경로").</summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>로그인 실패 메시지 코드. 사용자 ID가 없는지, 비밀번호가 틀렸는지 구분하지 않는다.</summary>
    public const string LoginFailedCode = "BC-E-001";
}
