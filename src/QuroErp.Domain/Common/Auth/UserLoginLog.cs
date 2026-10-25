namespace QuroErp.Domain.Common.Auth;

/// <summary>로그인 이력 1건 (sys_user_login_log). 성공·실패를 모두 남긴다.</summary>
/// <param name="AppUserId">찾은 사용자. 없는 사용자 ID로 실패하면 null.</param>
/// <param name="LoginUserId">입력한 사용자 ID.</param>
public sealed record UserLoginLog(DateTimeOffset LogDt, long? AppUserId, string LoginUserId, string? IpAddr, bool Success);
