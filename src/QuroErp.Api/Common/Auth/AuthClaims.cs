using System.Globalization;
using System.Security.Claims;

namespace QuroErp.Api.Common.Auth;

/// <summary>토큰·API 키 인증이 만드는 클레임 이름.</summary>
public static class AuthClaims
{
    /// <summary>sys_app_user.id (JWT sub).</summary>
    public const string Subject = "sub";

    /// <summary>사용자 ID 또는 API 클라이언트 코드.</summary>
    public const string Name = "name";

    /// <summary>sys_role.role_code.</summary>
    public const string Role = "role";

    /// <summary>"true" while the user must change the password. Other APIs then return 403 AUTH-005.</summary>
    public const string MustChangePassword = "pwd_change";

    public const string True = "true";

    /// <summary>sys_app_user.id를 읽는다. JWT로 인증한 요청에서만 쓴다.</summary>
    public static long GetAppUserId(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(Subject) ?? throw new InvalidOperationException("sub 클레임이 없다."), CultureInfo.InvariantCulture);
}
