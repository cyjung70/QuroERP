using System.Data;
using Dapper;
using QuroErp.Application.Common.Auth;

namespace QuroErp.Infrastructure.Common.Auth;

internal sealed class PasswordHistoryRepository : IPasswordHistoryRepository
{
    private const string SelectRecent = """
        SELECT password_hash
        FROM sys_password_history
        WHERE app_user_id = @AppUserId
        ORDER BY created_at DESC, id DESC
        LIMIT @Count
        """;

    private const string Insert = """
        INSERT INTO sys_password_history (app_user_id, password_hash, created_by, updated_by)
        VALUES (@AppUserId, @PasswordHash, @AuditUser, @AuditUser)
        """;

    public async Task<IReadOnlyList<string>> FindRecentAsync(
        long appUserId, int count, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<string>(new CommandDefinition(
            SelectRecent, new { AppUserId = appUserId, Count = count }, transaction, cancellationToken: cancellationToken))).AsList();

    public Task InsertAsync(
        long appUserId, string passwordHash, string auditUser, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Insert, new { AppUserId = appUserId, PasswordHash = passwordHash, AuditUser = auditUser }, transaction,
            cancellationToken: cancellationToken));
}
