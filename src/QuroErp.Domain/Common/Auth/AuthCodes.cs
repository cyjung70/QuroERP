namespace QuroErp.Domain.Common.Auth;

/// <summary>Authentication message codes (login.md "메시지").</summary>
public static class AuthCodes
{
    /// <summary>Required input is empty. Arg: field.</summary>
    public const string Required = "AUTH-001";

    /// <summary>Unknown ID or wrong password. The two are not told apart.</summary>
    public const string InvalidCredentials = "AUTH-002";

    /// <summary>Account locked. Arg: unlock time.</summary>
    public const string Locked = "AUTH-003";

    /// <summary>Account not in use or past its valid date.</summary>
    public const string Inactive = "AUTH-004";

    public const string PasswordChangeRequired = "AUTH-005";

    public const string SessionExpired = "AUTH-006";

    public const string AutoLoginExpired = "AUTH-007";

    public const string TooManyRequests = "AUTH-008";
}

/// <summary>sys_user_login_log.event_type values.</summary>
public static class LoginEvents
{
    public const string LoginOk = "LOGIN_OK";
    public const string LoginFail = "LOGIN_FAIL";
    public const string Locked = "LOCKED";
    public const string AutoLogin = "AUTO_LOGIN";
    public const string Logout = "LOGOUT";
    public const string Unlock = "UNLOCK";
}
