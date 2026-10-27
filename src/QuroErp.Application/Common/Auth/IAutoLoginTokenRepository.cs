using System.Data;

namespace QuroErp.Application.Common.Auth;

public interface IAutoLoginTokenRepository
{
    /// <summary>Finds a token by hash, revoked or expired ones included.</summary>
    /// <param name="forUpdate">Locks the row. Lock the user row first so every path locks user → tokens.</param>
    Task<AutoLoginTokenRow?> FindByHashAsync(string tokenHash, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken);

    Task InsertAsync(
        long appUserId, string tokenHash, DateTimeOffset expiresAt, string? userAgent, DateTimeOffset now,
        IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Revokes one token of the user if it is not revoked yet.</summary>
    Task RevokeAsync(long appUserId, string tokenHash, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Revokes every token of the user that is not revoked yet.</summary>
    Task RevokeAllAsync(long appUserId, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken);
}
