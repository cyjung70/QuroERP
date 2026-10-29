using System.Data;
using Dapper;
using QuroErp.Application.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

internal sealed class AutoLoginTokenRepository : IAutoLoginTokenRepository
{
    private const string SelectByHash = """
        SELECT id, app_user_id, expires_at, revoked_at, created_at
        FROM sys_auto_login_token
        WHERE token_hash = @TokenHash
        """;

    private const string LockByHash = SelectByHash + " FOR UPDATE";

    private const string Insert = """
        INSERT INTO sys_auto_login_token (app_user_id, token_hash, expires_at, user_agent, created_at, created_by, updated_at, updated_by)
        VALUES (@AppUserId, @TokenHash, @ExpiresAt, @UserAgent, @Now, @AuditUser, @Now, @AuditUser)
        """;

    private const string Revoke = """
        UPDATE sys_auto_login_token
        SET revoked_at = @Now, updated_at = @Now, updated_by = @AuditUser
        WHERE app_user_id = @AppUserId AND token_hash = @TokenHash AND revoked_at IS NULL
        """;

    private const string RevokeAll = """
        UPDATE sys_auto_login_token
        SET revoked_at = @Now, updated_at = @Now, updated_by = @AuditUser
        WHERE app_user_id = @AppUserId AND revoked_at IS NULL
        """;

    private const string RevokeEveryUser = """
        UPDATE sys_auto_login_token
        SET revoked_at = @Now, updated_at = @Now, updated_by = @AuditUser
        WHERE revoked_at IS NULL
        """;

    private const string AuditUser = UserLoginLogRepository.AuditUser;

    public async Task<AutoLoginTokenRow?> FindByHashAsync(
        string tokenHash, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forUpdate ? LockByHash : SelectByHash, new { TokenHash = tokenHash }, transaction, cancellationToken: cancellationToken));
        return row is null ? null : new AutoLoginTokenRow(row.Id, row.AppUserId, row.ExpiresAt, row.RevokedAt, row.CreatedAt);
    }

    public Task InsertAsync(
        long appUserId, string tokenHash, DateTimeOffset expiresAt, string? userAgent, DateTimeOffset now,
        IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert,
            new
            {
                AppUserId = appUserId, TokenHash = tokenHash, ExpiresAt = expiresAt,
                UserAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent, Now = now, AuditUser,
            },
            transaction,
            cancellationToken: cancellationToken));

    public Task RevokeAsync(long appUserId, string tokenHash, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Revoke, new { AppUserId = appUserId, TokenHash = tokenHash, Now = now, AuditUser }, transaction, cancellationToken: cancellationToken));

    public Task RevokeAllAsync(long appUserId, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            RevokeAll, new { AppUserId = appUserId, Now = now, AuditUser }, transaction, cancellationToken: cancellationToken));

    public Task RevokeEveryUserAsync(DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            RevokeEveryUser, new { Now = now, AuditUser }, transaction, cancellationToken: cancellationToken));

    // Npgsql reads timestamptz as DateTime, which Dapper converts only for properties, not constructor parameters
    private sealed class Row
    {
        public long Id { get; init; }

        public long AppUserId { get; init; }

        public DateTimeOffset ExpiresAt { get; init; }

        public DateTimeOffset? RevokedAt { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }
}
