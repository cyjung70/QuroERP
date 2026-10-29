using System.Data;

namespace QuroErp.Application.Common.Auth;

/// <summary>Previous password hashes of a user (sys_password_history, BC-BR-055).</summary>
public interface IPasswordHistoryRepository
{
    /// <summary>The newest <paramref name="count"/> previous hashes, newest first.</summary>
    Task<IReadOnlyList<string>> FindRecentAsync(long appUserId, int count, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <param name="auditUser">User ID written to created_by and updated_by.</param>
    Task InsertAsync(long appUserId, string passwordHash, string auditUser, IDbTransaction transaction, CancellationToken cancellationToken);
}
