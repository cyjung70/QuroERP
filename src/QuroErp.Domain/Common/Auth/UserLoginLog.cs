namespace QuroErp.Domain.Common.Auth;

/// <summary>로그인 이력 1건 (sys_user_login_log). 성공·실패를 모두 남긴다.</summary>
/// <param name="AppUserId">찾은 사용자. 없는 사용자 ID로 실패하면 null.</param>
/// <param name="LoginUserId">입력한 사용자 ID.</param>
/// <param name="EventType">One of <see cref="LoginEvents"/>.</param>
/// <param name="ResultCode">AUTH-xxx for a failure, null for a success.</param>
public sealed record UserLoginLog(
    DateTimeOffset LogDt,
    long? AppUserId,
    string LoginUserId,
    string? IpAddr,
    bool Success,
    string EventType,
    string? ResultCode = null,
    string? UserAgent = null);
