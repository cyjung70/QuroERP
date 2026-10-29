namespace QuroErp.Contracts.Common.Auth;

/// <summary>Login request (POST /api/auth/login). Empty fields return 400 AUTH-001.</summary>
/// <param name="LoginId">Typed user ID. The server trims and lowercases it (max 50 characters).</param>
/// <param name="Password">Not trimmed (max 128 characters).</param>
/// <param name="AutoLogin">"Keep me signed in". Ignored while auto login is off in the settings.</param>
/// <param name="Language">Language chosen on the login screen (ko, en). Messages use it and the user's language is updated.</param>
public sealed record LoginRequest(string? LoginId, string? Password, bool AutoLogin = false, string? Language = null);

/// <summary>Auto-login request (POST /api/auth/auto-login) with the raw token from the token cookie.</summary>
public sealed record AutoLoginRequest(string? Token);

/// <summary>Logout request (POST /api/auth/logout). The token of this device, if any, is revoked.</summary>
public sealed record LogoutRequest(string? AutoLoginToken);

/// <summary>
/// Password change (POST /api/auth/change-password, BC-BR-051). The current password is required even for a forced change
/// (BC-BR-052). Empty fields return 400 AUTH-001; rule violations 400 AUTH-011~015 (BC-BR-059).
/// </summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>로그인·재발급·비밀번호 변경 응답. AccessToken은 Web 서버만 보관하고 브라우저에 보내지 않는다.</summary>
/// <param name="Language">User language (ko, en). Null when not set.</param>
/// <param name="LastLoginAt">Previous login before this one, shown on the main screen.</param>
/// <param name="AutoLoginToken">New raw auto-login token for the token cookie. Null when none was issued.</param>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string UserId,
    string? UserName,
    string? RoleCode,
    bool MustChangePassword,
    string? Language,
    DateTimeOffset? LastLoginAt = null,
    string? LastLoginIp = null,
    string? AutoLoginToken = null,
    DateTimeOffset? AutoLoginExpiresAt = null);

/// <summary>
/// Login screen settings (GET /api/auth/login-options, anonymous). Only these three settings leave the server before sign-in.
/// </summary>
/// <param name="AutoLoginEnabled">Shows the "keep me signed in" checkbox (auto_login_enabled).</param>
/// <param name="PasswordResetMode">ADMIN_ONLY (contact the administrator) or EMAIL (password_reset_mode).</param>
/// <param name="SessionIdleMinutes">Idle minutes before the session ends (session_idle_minutes).</param>
public sealed record LoginOptionsResponse(bool AutoLoginEnabled, string PasswordResetMode, int SessionIdleMinutes);

/// <summary>Signed-in user (GET /api/auth/me).</summary>
/// <param name="Menus">Menus the role may open, with the access level of each (the user's permissions).</param>
public sealed record MeResponse(
    string UserId,
    string? UserName,
    string? RoleCode,
    string? Language,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    string? LastLoginIp,
    AuthWarehouse? DefaultWarehouse,
    IReadOnlyList<AuthMenu> Menus);

/// <summary>A menu of the user's role (sys_role_menu, sys_menu).</summary>
public sealed record AuthMenu(string MenuCode, string? Name, string? ProgramId, string? Module, string? ParentMenuCode, string? FullAccess);

public sealed record AuthWarehouse(long Id, string WarehouseCode, string WarehouseName);
