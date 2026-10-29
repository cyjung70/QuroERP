namespace QuroErp.Domain.Common.Auth;

/// <summary>사용자 (sys_app_user). 비밀번호는 해시만 가진다.</summary>
public sealed class AppUser
{
    /// <summary>사용 여부 코드 VALID_FLAG의 '사용' 값.</summary>
    public const string ValidFlagUsed = "Y";

    /// <summary>Longest user ID after normalization (login.md).</summary>
    public const int MaxUserIdLength = 50;

    /// <summary>Longest password (login.md). Passwords are not trimmed.</summary>
    public const int MaxPasswordLength = 128;

    public long Id { get; init; }

    public string UserId { get; init; } = "";

    public string PasswordHash { get; init; } = "";

    public bool MustChangePassword { get; init; }

    public string? UserName { get; init; }

    public string? RoleCode { get; init; }

    public string? ValidFlag { get; init; }

    public DateOnly? UserValidDt { get; init; }

    public DateOnly? PasswordValidDt { get; init; }

    /// <summary>사용자 언어 설정 (ko, en). null이면 브라우저 언어를 따른다.</summary>
    public string? LangCode { get; init; }

    public DateTimeOffset? PasswordChangedAt { get; init; }

    /// <summary>Default warehouse. Null means the company default warehouse.</summary>
    public long? DefaultWarehouseId { get; init; }

    /// <summary>
    /// Goes up when the user's tokens must stop working (password change, reset, not in use, forced revoke).
    /// Token refresh fails for a token that carries an older version (BC-BR-058, BC-BR-130).
    /// </summary>
    public int TokenVersion { get; init; }

    /// <summary>Consecutive failed logins. Reset to 0 on success.</summary>
    public int FailedCount { get; private set; }

    /// <summary>Null means not locked.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>Previous login, shown on the main screen.</summary>
    public DateTimeOffset? LastLoginAt { get; private set; }

    public string? LastLoginIp { get; private set; }

    /// <summary>Trims and lowercases a typed user ID (login.md "로그인 처리" 3).</summary>
    public static string NormalizeUserId(string loginId) => loginId.Trim().ToLowerInvariant();

    /// <summary>
    /// 로그인할 수 있는 사용자인지 본다: 사용 여부가 'Y'이고, 사용자 유효일이 지나지 않았다.
    /// [추정] valid_flag가 비어 있으면 사용하지 않는 사용자로 본다.
    /// </summary>
    public bool CanLogin(DateOnly today) =>
        ValidFlag == ValidFlagUsed && (UserValidDt is null || UserValidDt >= today);

    /// <summary>비밀번호를 바꿔야 하는지 본다: 초기화·이관 표시가 있거나 비밀번호 유효일이 지났다.</summary>
    public bool RequiresPasswordChange(DateOnly today) =>
        MustChangePassword || (PasswordValidDt is not null && PasswordValidDt < today);

    /// <summary>
    /// Also forces a change when password_expire_days &gt; 0 and the password is older than that.
    /// [추정] A password without password_changed_at does not expire.
    /// </summary>
    public bool RequiresPasswordChange(DateTimeOffset now, DateOnly today, AuthSetting setting) =>
        RequiresPasswordChange(today)
        || (setting.PasswordExpireDays > 0 && PasswordChangedAt is not null
            && PasswordChangedAt.Value.AddDays(setting.PasswordExpireDays) < now);

    /// <summary>The lock is still running. A locked account fails even with the right password.</summary>
    public bool IsLocked(DateTimeOffset now) => LockedUntil > now;

    /// <summary>Clears a lock whose time has passed and starts the failure count again.</summary>
    public void ReleaseExpiredLock(DateTimeOffset now)
    {
        if (LockedUntil is not null && LockedUntil <= now)
        {
            LockedUntil = null;
            FailedCount = 0;
        }
    }

    /// <summary>
    /// Counts a wrong password. Reaching the limit locks the account until now + lockout minutes.
    /// Callers must not call this while <see cref="IsLocked"/>: attempts during a lock neither count nor extend it.
    /// </summary>
    /// <returns>True when this failure locked the account.</returns>
    public bool RecordFailure(DateTimeOffset now, AuthSetting setting)
    {
        FailedCount++;
        if (FailedCount < setting.MaxFailedAttempts)
        {
            return false;
        }
        LockedUntil = now.AddMinutes(setting.LockoutMinutes);
        return true;
    }

    /// <summary>Attempts left before the lock (for show_remaining_attempts).</summary>
    public int RemainingAttempts(AuthSetting setting) => Math.Max(0, setting.MaxFailedAttempts - FailedCount);

    /// <summary>Successful login: resets the failures and stores this login as the last one.</summary>
    public void RecordSuccess(DateTimeOffset now, string? ipAddr)
    {
        FailedCount = 0;
        LockedUntil = null;
        LastLoginAt = now;
        LastLoginIp = ipAddr;
    }
}
