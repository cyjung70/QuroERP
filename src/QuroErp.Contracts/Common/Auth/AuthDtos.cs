using System.ComponentModel.DataAnnotations;

namespace QuroErp.Contracts.Common.Auth;

/// <summary>로그인 요청 (POST /api/common/auth/login).</summary>
public sealed record LoginRequest(
    [Required, StringLength(100)] string UserId,
    [Required, StringLength(200)] string Password);

/// <summary>로그인·재발급 응답. AccessToken은 Web 서버만 보관하고 브라우저에 보내지 않는다.</summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string UserId,
    string? UserName,
    string? RoleCode,
    bool MustChangePassword);
