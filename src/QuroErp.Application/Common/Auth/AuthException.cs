namespace QuroErp.Application.Common.Auth;

/// <summary>
/// Login, auto-login or session failure (login.md). The API maps Code to the HTTP status
/// (AUTH-001 400, AUTH-002 401, AUTH-003 423, AUTH-004 403, AUTH-006·007 401, AUTH-011~015 400) and writes title and detail in the request language.
/// </summary>
public sealed class AuthException(string code, params object[] args) : Exception(code)
{
    public string Code { get; } = code;

    /// <summary>Message arguments. AUTH-001 carries an <see cref="AuthFields"/> value, AUTH-011·012 a number.</summary>
    public IReadOnlyList<object> Args { get; } = args;

    /// <summary>AUTH-003: when the lock ends.</summary>
    public DateTimeOffset? LockedUntil { get; init; }

    /// <summary>AUTH-002 with show_remaining_attempts: attempts left before the lock.</summary>
    public int? RemainingAttempts { get; init; }
}

/// <summary>Login and password change input fields named by AUTH-001.</summary>
public static class AuthFields
{
    public const string LoginId = "LoginId";
    public const string Password = "Password";
    public const string CurrentPassword = "CurrentPassword";
    public const string NewPassword = "NewPassword";
}
