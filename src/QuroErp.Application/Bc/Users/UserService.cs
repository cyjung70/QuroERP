using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Users;
using QuroErp.Domain.Bc.UserProfiles;
using QuroErp.Domain.Bc.Users;
using QuroErp.Domain.Common;
using QuroErp.Domain.Common.Auth;
using QuroErp.Domain.Common.Codes;

namespace QuroErp.Application.Bc.Users;

/// <summary>
/// User management (BC-UC-004, BC-BR-121~132). Every write locks the user row before the user's tokens, as the login paths do.
/// </summary>
public sealed class UserService(
    IUserRepository users,
    IUserProfileRepository profiles,
    ICodeValidator codes,
    IAutoLoginTokenRepository autoLoginTokens,
    IUserLoginLogRepository loginLogs,
    IPasswordHistoryRepository passwordHistory,
    IAuthSettingRepository settings,
    IPasswordHashService passwords,
    IUnitOfWorkFactory unitOfWork,
    TimeProvider clock)
{
    /// <summary>The user does not exist. Arg: id.</summary>
    public const string NotFoundCode = "Common-E-006";

    public async Task<UserListDto> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(query.Size, 1, UserRoutes.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var (rows, total) = await users.SearchAsync(
            new UserSearch(query.UserId, query.UserName, query.RoleId, query.ValidFlag, (page - 1) * size, size), uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return new UserListDto(rows.Select(ToDto).ToList(), total);
    }

    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<UserDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await users.FindAsync(id, forUpdate: false, uow.Transaction, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        await uow.CommitAsync(cancellationToken);
        return ToDto(row);
    }

    /// <summary>The user before or after the given one in user ID order; null at either end.</summary>
    public async Task<UserDto?> GetAdjacentAsync(long id, bool forward, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var row = await users.FindAdjacentAsync(id, forward, uow.Transaction, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return row is null ? null : ToDto(row);
    }

    /// <summary>New user with the administrator's initial password; the user must change it at sign-in (BC-BR-121~123).</summary>
    /// <exception cref="DomainException">BC-E-121 (user ID used), BC-E-122 (field), BC-E-405 (language), Common-E-002 (valid flag).</exception>
    /// <exception cref="AuthException">AUTH-001 without a password, AUTH-011~014 when it breaks the password rules.</exception>
    public async Task<UserDto> CreateAsync(CreateUserRequest request, string createdBy, CancellationToken cancellationToken)
    {
        var userId = UserAccount.NormalizeUserId(request.UserId);
        var fields = UserAccount.Normalize(ToFields(request));
        if (string.IsNullOrEmpty(request.Password))
        {
            throw new AuthException(AuthCodes.Required, AuthFields.Password);
        }
        await codes.EnsureValidAsync(CodeMajors.ValidFlag, fields.ValidFlag, cancellationToken);

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        await EnsureReferencesAsync(fields, current: null, tx, cancellationToken);
        if (await users.UserIdExistsAsync(userId, tx, cancellationToken))
        {
            throw new DomainException(UserAccount.DuplicateUserIdCode, userId);
        }
        // A new user has no earlier password to reuse
        var setting = await settings.GetAsync(tx, cancellationToken) ?? new AuthSetting();
        if (PasswordPolicy.FirstViolation(request.Password, userId, setting, () => false) is { } violation)
        {
            throw new AuthException(violation.Code, violation.Args);
        }

        var id = await users.InsertAsync(userId, passwords.Hash(request.Password), fields, createdBy, tx, cancellationToken);
        var saved = await users.FindAsync(id, forUpdate: false, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!);
    }

    /// <summary>
    /// Saves the fields; never the user ID or the password (BC-BR-121, 124). Leaving "in use" ends the user's sign-ins (BC-BR-125).
    /// </summary>
    /// <param name="actorAppUserId">The signed-in administrator, for BC-BR-131.</param>
    /// <exception cref="DomainException">BC-E-122 (field), BC-E-131 (own account), BC-E-405 (language), Common-E-002 (valid flag).</exception>
    /// <exception cref="ConcurrencyException">updatedAt differs from the stored row.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task<UserDto> UpdateAsync(
        long id, UpdateUserRequest request, long actorAppUserId, string updatedBy, CancellationToken cancellationToken)
    {
        var fields = UserAccount.Normalize(ToFields(request));
        await codes.EnsureValidAsync(CodeMajors.ValidFlag, fields.ValidFlag, cancellationToken);
        var now = clock.GetUtcNow();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var row = await users.FindAsync(id, forUpdate: true, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.UpdatedAt != request.UpdatedAt)
        {
            throw new ConcurrencyException();
        }
        UserAccount.EnsureSelfChangeAllowed(id == actorAppUserId, row.RoleId, fields);
        await EnsureReferencesAsync(fields, row, tx, cancellationToken);

        var stopsUse = UserAccount.StopsUse(row.ValidFlag, fields);
        if (!await users.UpdateAsync(id, fields, stopsUse, request.UpdatedAt, updatedBy, tx, cancellationToken))
        {
            throw new ConcurrencyException();
        }
        if (stopsUse)
        {
            await autoLoginTokens.RevokeAllAsync(id, now, tx, cancellationToken);
        }
        var saved = await users.FindAsync(id, forUpdate: false, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return ToDto(saved!);
    }

    /// <summary>
    /// Clears the failures and the lock and logs UNLOCK with the administrator's IP (BC-BR-128).
    /// An account that is not locked does not change.
    /// </summary>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task UnlockAsync(long id, AuthClient client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var row = await users.FindAsync(id, forUpdate: true, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        if (row.LockedUntil is not { } lockedUntil || lockedUntil <= now)
        {
            return;
        }
        await users.UnlockAsync(id, tx, cancellationToken);
        await loginLogs.InsertAsync(
            new UserLoginLog(now, row.Id, row.UserId, client.IpAddr, true, LoginEvents.Unlock, null, client.UserAgent), tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The administrator's new password with the password rules, must change at sign-in, all auto-login tokens revoked
    /// and the token version raised, in one transaction (BC-BR-129). The old password goes to the history (BC-BR-055).
    /// </summary>
    /// <exception cref="AuthException">AUTH-001 without a password, AUTH-011~015 when it breaks the password rules.</exception>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task ResetPasswordAsync(long id, ResetPasswordRequest request, string updatedBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.NewPassword))
        {
            throw new AuthException(AuthCodes.Required, AuthFields.NewPassword);
        }
        var newPassword = request.NewPassword;
        var now = clock.GetUtcNow();

        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        var row = await users.FindAsync(id, forUpdate: true, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        var currentHash = await users.FindPasswordHashAsync(id, tx, cancellationToken) ?? "";
        var setting = await settings.GetAsync(tx, cancellationToken) ?? new AuthSetting();
        // BC-BR-055: the history count includes the current password
        var recent = setting.PasswordHistoryCount > 1
            ? await passwordHistory.FindRecentAsync(id, setting.PasswordHistoryCount - 1, tx, cancellationToken)
            : [];
        var violation = PasswordPolicy.FirstViolation(newPassword, row.UserId, setting, () =>
            setting.PasswordHistoryCount > 0
            && (passwords.Verify(currentHash, newPassword) || recent.Any(hash => passwords.Verify(hash, newPassword))));
        if (violation is not null)
        {
            throw new AuthException(violation.Code, violation.Args);
        }

        await passwordHistory.InsertAsync(id, currentHash, updatedBy, tx, cancellationToken);
        await users.ResetPasswordAsync(id, passwords.Hash(newPassword), now, updatedBy, tx, cancellationToken);
        await autoLoginTokens.RevokeAllAsync(id, now, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Revokes all the user's auto-login tokens and raises the token version (BC-BR-130). The user's other sessions
    /// must sign in again at the next token refresh.
    /// </summary>
    /// <exception cref="NotFoundException">Common-E-006.</exception>
    public async Task RevokeTokensAsync(long id, CancellationToken cancellationToken)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken);
        var tx = uow.Transaction;
        _ = await users.FindAsync(id, forUpdate: true, tx, cancellationToken) ?? throw new NotFoundException(NotFoundCode, id);
        await autoLoginTokens.RevokeAllAsync(id, clock.GetUtcNow(), tx, cancellationToken);
        await users.RaiseTokenVersionAsync(id, tx, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }

    // BC-BR-122 role in use, BC-BR-403 language in use. [추정] On update only a changed value is checked,
    // so a role or language that went out of use later does not block other edits of the user.
    private async Task EnsureReferencesAsync(
        UserAccountFields fields, UserRow? current, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (fields.RoleId is { } roleId && roleId != current?.RoleId && !await users.IsRoleInUseAsync(roleId, transaction, cancellationToken))
        {
            throw new DomainException(UserAccount.InvalidFieldCode, UserAccount.RoleIdField);
        }
        if (fields.LangCode is { } lang && lang != current?.LangCode && !await profiles.IsLanguageInUseAsync(lang, transaction, cancellationToken))
        {
            throw new DomainException(UserProfile.LanguageNotInUseCode, lang);
        }
    }

    private static UserAccountFields ToFields(UserFieldsRequest r) =>
        new(r.UserName, r.RoleId, r.Email, r.Phone, r.LangCode, r.UserValidDt, r.ValidFlag ?? "");

    private static UserDto ToDto(UserRow r) => new(
        r.Id, r.UserId, r.UserName, r.RoleId, r.RoleCode, r.Email, r.Phone, r.LangCode, r.UserValidDt, r.ValidFlag, r.MustChangePassword,
        r.FailedCount, r.LockedUntil, r.LastLoginAt, r.LastLoginIp, r.UpdatedAt);
}
