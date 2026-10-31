using QuroErp.Contracts.Common;

namespace QuroErp.Contracts.Bc.Users;

/// <summary>User management API routes (BC-UC-004).</summary>
public static class UserRoutes
{
    /// <summary>GET list (query <see cref="UserSearchQuery"/>), POST new user. There is no DELETE (BC-BR-125).</summary>
    public const string Base = "api/bc/users";

    /// <summary>GET {Base}/{id}/previous and {Base}/{id}/next: the user before or after it in user ID order. 204 when there is none.</summary>
    public const string Previous = "previous";
    public const string Next = "next";

    /// <summary>POST {Base}/{id}/unlock (BC-BR-128), reset-password (BC-BR-129), revoke-tokens (BC-BR-130). 204.</summary>
    public const string Unlock = "unlock";
    public const string ResetPassword = "reset-password";
    public const string RevokeTokens = "revoke-tokens";

    /// <summary>Default and largest page size (api.md "기타").</summary>
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
}

/// <summary>
/// One user (sys_app_user). The password hash never leaves the server (BC-BR-132), and there is no per-user password
/// expiry date (BC-BR-127).
/// </summary>
/// <param name="UserValidDt">Last day the account may sign in. Null means no end (BC-BR-126).</param>
/// <param name="LockedUntil">Null when not locked. Unlock with <see cref="UserRoutes.Unlock"/> (BC-BR-128).</param>
public sealed record UserDto(
    long Id,
    string UserId,
    string? UserName,
    long? RoleId,
    string? RoleCode,
    string? Email,
    string? Phone,
    string? LangCode,
    DateOnly? UserValidDt,
    string? ValidFlag,
    bool MustChangePassword,
    int FailedCount,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLoginAt,
    string? LastLoginIp,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy) : AuditInfo(CreatedAt, CreatedBy, UpdatedAt, UpdatedBy);

public sealed record UserListDto(IReadOnlyList<UserDto> Items, int Total);

/// <summary>List conditions. All are optional.</summary>
public sealed class UserSearchQuery
{
    /// <summary>User ID starts with this (case-insensitive).</summary>
    public string? UserId { get; init; }

    /// <summary>User name contains this (case-insensitive).</summary>
    public string? UserName { get; init; }

    public long? RoleId { get; init; }

    public string? ValidFlag { get; init; }

    /// <summary>1-based.</summary>
    public int Page { get; init; } = 1;

    public int Size { get; init; } = UserRoutes.DefaultPageSize;
}

/// <summary>Fields saved on create and update (BC-BR-122, 126, 403).</summary>
/// <param name="ValidFlag">VALID_FLAG code. Blank means Y (in use).</param>
public abstract record UserFieldsRequest(
    string? UserName, long? RoleId, string? Email, string? Phone, string? LangCode, DateOnly? UserValidDt, string? ValidFlag);

/// <summary>New user with the initial password the administrator enters (BC-BR-121, 123). The user must change it at sign-in.</summary>
public sealed record CreateUserRequest(
    string? UserId, string? Password, string? UserName, long? RoleId, string? Email, string? Phone, string? LangCode,
    DateOnly? UserValidDt, string? ValidFlag)
    : UserFieldsRequest(UserName, RoleId, Email, Phone, LangCode, UserValidDt, ValidFlag);

/// <summary>Update. The user ID and the password cannot change here (BC-BR-121, 124). updatedAt is required (409 when stale).</summary>
public sealed record UpdateUserRequest(
    string? UserName, long? RoleId, string? Email, string? Phone, string? LangCode, DateOnly? UserValidDt, string? ValidFlag,
    DateTimeOffset UpdatedAt)
    : UserFieldsRequest(UserName, RoleId, Email, Phone, LangCode, UserValidDt, ValidFlag);

/// <summary>Password reset by the administrator (BC-BR-129). The password rules apply; the user must change it at sign-in.</summary>
public sealed record ResetPasswordRequest(string? NewPassword);
