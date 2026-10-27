using System.Data;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Common.Auth;

/// <summary>Login, auto login, logout, current user and token refresh (BC-UC-001, login.md, security.md).</summary>
/// <remarks>
/// Failures throw <see cref="AuthException"/> after the failure log and the counters are committed.
/// Raw passwords and tokens are never logged or stored.
/// </remarks>
public sealed class LoginService(
    IAppUserRepository users,
    IUserLoginLogRepository loginLogs,
    IAuthSettingRepository settings,
    IAutoLoginTokenRepository autoLoginTokens,
    IPasswordHashService passwords,
    IAccessTokenIssuer tokens,
    IUnitOfWorkFactory unitOfWork,
    TimeProvider clock)
{
    private static readonly string[] Languages = ["ko", "en"];

    /// <summary>
    /// Checks the ID and password in the order of login.md "로그인 처리": account → lock → password → status.
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, AuthClient client, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LoginId))
        {
            throw new AuthException(AuthCodes.Required, AuthFields.LoginId);
        }
        if (string.IsNullOrEmpty(request.Password))
        {
            throw new AuthException(AuthCodes.Required, AuthFields.Password);
        }

        var userId = AppUser.NormalizeUserId(request.LoginId);
        var password = request.Password;
        var now = clock.GetUtcNow();
        var today = Today();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var setting = await GetSettingAsync(tx, cancellationToken);
        var user = userId.Length <= AppUser.MaxUserIdLength
            ? await users.LockByUserIdAsync(userId, tx, cancellationToken)
            : null;

        AuthException? failure = null;
        var eventType = LoginEvents.LoginFail;
        if (user is null)
        {
            // Hash anyway so the response time does not tell whether the ID exists. Unknown IDs never lock
            passwords.Hash(password);
            failure = new AuthException(AuthCodes.InvalidCredentials);
        }
        else
        {
            user.ReleaseExpiredLock(now);
            if (user.IsLocked(now))
            {
                // Attempts during a lock neither count nor extend it
                failure = LockedFailure(user);
            }
            else if (password.Length > AppUser.MaxPasswordLength || !passwords.Verify(user.PasswordHash, password))
            {
                if (user.RecordFailure(now, setting))
                {
                    eventType = LoginEvents.Locked;
                    failure = LockedFailure(user);
                }
                else
                {
                    failure = new AuthException(AuthCodes.InvalidCredentials)
                    {
                        RemainingAttempts = setting.ShowRemainingAttempts ? user.RemainingAttempts(setting) : null,
                    };
                }
            }
            else if (!user.CanLogin(today))
            {
                // Status is checked only after a correct password
                failure = new AuthException(AuthCodes.Inactive);
            }
        }

        if (failure is not null)
        {
            if (user is not null)
            {
                await users.SaveLoginStateAsync(user, null, tx, cancellationToken);
            }
            await loginLogs.InsertAsync(
                new UserLoginLog(now, user?.Id, request.LoginId, client.IpAddr, false, eventType, failure.Code, client.UserAgent),
                tx, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            throw failure;
        }

        var previousAt = user!.LastLoginAt;
        var previousIp = user.LastLoginIp;
        var language = Language(request.Language);
        user.RecordSuccess(now, client.IpAddr);
        await users.SaveLoginStateAsync(user, language, tx, cancellationToken);

        (string Token, DateTimeOffset ExpiresAt)? autoLogin = null;
        if (request.AutoLogin && setting.AutoLoginEnabled)
        {
            autoLogin = await IssueAutoLoginTokenAsync(user, setting, client, now, tx, cancellationToken);
        }

        await loginLogs.InsertAsync(
            new UserLoginLog(now, user.Id, request.LoginId, client.IpAddr, true, LoginEvents.LoginOk, null, client.UserAgent),
            tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return ToResponse(user, language ?? user.LangCode, setting, now, today, previousAt, previousIp, autoLogin);
    }

    /// <summary>
    /// Signs in with an auto-login token and replaces it with a new one (login.md "자동 로그인").
    /// A revoked token coming back is treated as stolen: every token of that user is revoked.
    /// </summary>
    public async Task<LoginResponse> AutoLoginAsync(AutoLoginRequest request, AuthClient client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = Today();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var setting = await GetSettingAsync(tx, cancellationToken);
        if (!setting.AutoLoginEnabled || string.IsNullOrEmpty(request.Token))
        {
            throw new AuthException(AuthCodes.AutoLoginExpired);
        }

        var tokenHash = AutoLoginToken.Hash(request.Token);
        var found = await autoLoginTokens.FindByHashAsync(tokenHash, false, tx, cancellationToken);
        if (found is null)
        {
            throw new AuthException(AuthCodes.AutoLoginExpired);
        }

        // Lock order user → tokens, the same as login and revoke-all
        var user = await users.LockByIdAsync(found.AppUserId, tx, cancellationToken);
        var token = await autoLoginTokens.FindByHashAsync(tokenHash, true, tx, cancellationToken);
        if (user is null || token is null)
        {
            throw new AuthException(AuthCodes.AutoLoginExpired);
        }

        user.ReleaseExpiredLock(now);
        var valid = false;
        if (token.RevokedAt is not null)
        {
            await autoLoginTokens.RevokeAllAsync(user.Id, now, tx, cancellationToken);
        }
        else if (token.ExpiresAt <= now
            || user.PasswordChangedAt > token.CreatedAt
            || user.IsLocked(now)
            || !user.CanLogin(today))
        {
            await autoLoginTokens.RevokeAsync(user.Id, tokenHash, now, tx, cancellationToken);
        }
        else
        {
            valid = true;
        }

        if (!valid)
        {
            await users.SaveLoginStateAsync(user, null, tx, cancellationToken);
            await loginLogs.InsertAsync(
                new UserLoginLog(now, user.Id, user.UserId, client.IpAddr, false, LoginEvents.AutoLogin, AuthCodes.AutoLoginExpired, client.UserAgent),
                tx, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            throw new AuthException(AuthCodes.AutoLoginExpired);
        }

        var previousAt = user.LastLoginAt;
        var previousIp = user.LastLoginIp;
        await autoLoginTokens.RevokeAsync(user.Id, tokenHash, now, tx, cancellationToken);
        var rotated = await IssueAutoLoginTokenAsync(user, setting, client, now, tx, cancellationToken);
        user.RecordSuccess(now, client.IpAddr);
        await users.SaveLoginStateAsync(user, null, tx, cancellationToken);
        await loginLogs.InsertAsync(
            new UserLoginLog(now, user.Id, user.UserId, client.IpAddr, true, LoginEvents.AutoLogin, null, client.UserAgent),
            tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return ToResponse(user, user.LangCode, setting, now, today, previousAt, previousIp, rotated);
    }

    /// <summary>Revokes this device's auto-login token, if any, and logs LOGOUT. The saved user ID stays.</summary>
    public async Task LogoutAsync(long appUserId, LogoutRequest request, AuthClient client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var user = await users.FindByIdAsync(appUserId, tx, cancellationToken);
        if (user is null)
        {
            return;
        }
        if (!string.IsNullOrEmpty(request.AutoLoginToken))
        {
            await autoLoginTokens.RevokeAsync(user.Id, AutoLoginToken.Hash(request.AutoLoginToken), now, tx, cancellationToken);
        }
        await loginLogs.InsertAsync(
            new UserLoginLog(now, user.Id, user.UserId, client.IpAddr, true, LoginEvents.Logout, null, client.UserAgent),
            tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>User, permissions (role menus) and default warehouse. AUTH-006 if the user can no longer sign in.</summary>
    public async Task<MeResponse> MeAsync(long appUserId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = Today();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var user = await users.FindByIdAsync(appUserId, tx, cancellationToken);
        if (user is null || !user.CanLogin(today) || user.IsLocked(now))
        {
            throw new AuthException(AuthCodes.SessionExpired);
        }
        var setting = await GetSettingAsync(tx, cancellationToken);
        var menus = await users.FindMenusAsync(user.Id, tx, cancellationToken);
        var warehouse = await users.FindDefaultWarehouseAsync(user.Id, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return new MeResponse(
            user.UserId, user.UserName, user.RoleCode, user.LangCode, user.RequiresPasswordChange(now, today, setting),
            user.LastLoginAt, user.LastLoginIp, warehouse, menus);
    }

    /// <summary>로그인한 사용자의 토큰을 다시 발급한다. 그 사이 사용 중지·잠금된 사용자면 null.</summary>
    public async Task<LoginResponse?> RefreshAsync(long appUserId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = Today();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var user = await users.FindByIdAsync(appUserId, uow.Transaction, cancellationToken);
        var setting = await GetSettingAsync(uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return user is not null && user.CanLogin(today) && !user.IsLocked(now)
            ? ToResponse(user, user.LangCode, setting, now, today, user.LastLoginAt, user.LastLoginIp, null)
            : null;
    }

    // [추정] 업무 날짜는 서버 현지 날짜로 본다
    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private async Task<AuthSetting> GetSettingAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        await settings.GetAsync(transaction, cancellationToken) ?? new AuthSetting();

    private static string? Language(string? language) =>
        language is not null && Languages.Contains(language, StringComparer.OrdinalIgnoreCase) ? language.ToLowerInvariant() : null;

    private static AuthException LockedFailure(AppUser user) =>
        new(AuthCodes.Locked) { LockedUntil = user.LockedUntil };

    private async Task<(string Token, DateTimeOffset ExpiresAt)> IssueAutoLoginTokenAsync(
        AppUser user, AuthSetting setting, AuthClient client, DateTimeOffset now,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var token = AutoLoginToken.Create();
        var expiresAt = now.AddDays(setting.AutoLoginValidDays);
        await autoLoginTokens.InsertAsync(user.Id, AutoLoginToken.Hash(token), expiresAt, client.UserAgent, now, transaction, cancellationToken);
        return (token, expiresAt);
    }

    private LoginResponse ToResponse(
        AppUser user, string? language, AuthSetting setting, DateTimeOffset now, DateOnly today,
        DateTimeOffset? lastLoginAt, string? lastLoginIp, (string Token, DateTimeOffset ExpiresAt)? autoLogin)
    {
        var mustChangePassword = user.RequiresPasswordChange(now, today, setting);
        var token = tokens.Issue(user, mustChangePassword, now);
        return new LoginResponse(
            token.Token, token.ExpiresAt, user.UserId, user.UserName, user.RoleCode, mustChangePassword, language,
            lastLoginAt, lastLoginIp, autoLogin?.Token, autoLogin?.ExpiresAt);
    }
}
