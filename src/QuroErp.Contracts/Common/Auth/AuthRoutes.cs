namespace QuroErp.Contracts.Common.Auth;

/// <summary>인증 API 경로 (login.md, security.md).</summary>
public static class AuthRoutes
{
    public const string Base = "api/auth";
    public const string Login = Base + "/login";
    public const string AutoLogin = Base + "/auto-login";
    public const string Logout = Base + "/logout";
    public const string Me = Base + "/me";
    public const string Refresh = Base + "/refresh";
    public const string ChangePassword = Base + "/change-password";

    /// <summary>GET, anonymous: the settings the login screen needs before sign-in (login.md).</summary>
    public const string LoginOptions = Base + "/login-options";

    /// <summary>외부 서버가 쓰는 API 키 헤더 (security.md "인증 경로").</summary>
    public const string ApiKeyHeader = "X-Api-Key";
}
