namespace QuroErp.Domain.Common.Auth;

/// <summary>사용자 (sys_app_user). 비밀번호는 해시만 가진다.</summary>
public sealed class AppUser
{
    /// <summary>사용 여부 코드 VALID_FLAG의 '사용' 값.</summary>
    public const string ValidFlagUsed = "Y";

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

    /// <summary>
    /// 로그인할 수 있는 사용자인지 본다: 사용 여부가 'Y'이고, 사용자 유효일이 지나지 않았다.
    /// [추정] valid_flag가 비어 있으면 사용하지 않는 사용자로 본다.
    /// </summary>
    public bool CanLogin(DateOnly today) =>
        ValidFlag == ValidFlagUsed && (UserValidDt is null || UserValidDt >= today);

    /// <summary>비밀번호를 바꿔야 하는지 본다: 초기화·이관 표시가 있거나 비밀번호 유효일이 지났다.</summary>
    public bool RequiresPasswordChange(DateOnly today) =>
        MustChangePassword || (PasswordValidDt is not null && PasswordValidDt < today);
}
