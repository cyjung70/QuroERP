using System.Collections.Concurrent;
using System.Data;
using QuroErp.Application.Bc.Users;
using QuroErp.Domain.Bc.Users;

namespace QuroErp.Api.Tests.Bc.Users;

/// <summary>sys_app_user for user management in memory. Ids follow <see cref="ApiFactory"/>: 7 is the admin who signs in.</summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    public const long TargetId = 30;
    public const long AdminRoleId = 1;
    public const long UserRoleId = 2;

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    public ConcurrentDictionary<long, UserRow> Rows { get; } = new()
    {
        [7] = Row(7, ApiFactory.UserId, AdminRoleId),
        [ApiFactory.ProfileUserKey] = Row(ApiFactory.ProfileUserKey, ApiFactory.ProfileUserId, UserRoleId),
        [TargetId] = Row(TargetId, "target", UserRoleId),
    };

    public ConcurrentDictionary<long, string> Hashes { get; } = new();

    public ConcurrentQueue<long> TokenVersionRaised { get; } = new();

    public static UserRow Row(long id, string userId, long roleId, DateTimeOffset? lockedUntil = null) =>
        new(id, userId, userId, roleId, roleId == AdminRoleId ? "admin" : "user", null, null, "ko", null, "Y", false,
            lockedUntil is null ? 0 : 5, lockedUntil, null, null, Start);

    public Task<(IReadOnlyList<UserRow> Users, int Total)> SearchAsync(UserSearch search, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var matched = Rows.Values
            .Where(r => search.UserId is null || r.UserId.StartsWith(search.UserId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.UserId, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<(IReadOnlyList<UserRow>, int)>((matched.Skip(search.Offset).Take(search.Limit).ToList(), matched.Count));
    }

    public Task<UserRow?> FindAsync(long id, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<UserRow?> FindAdjacentAsync(long id, bool forward, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var current = Rows[id].UserId;
        var ordered = Rows.Values.OrderBy(r => r.UserId, StringComparer.Ordinal).ToList();
        return Task.FromResult(forward
            ? ordered.FirstOrDefault(r => string.CompareOrdinal(r.UserId, current) > 0)
            : ordered.LastOrDefault(r => string.CompareOrdinal(r.UserId, current) < 0));
    }

    public Task<string?> FindPasswordHashAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Hashes.GetValueOrDefault(id));

    public Task<bool> UserIdExistsAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Values.Any(r => r.UserId == userId));

    public Task<bool> IsRoleInUseAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(roleId is AdminRoleId or UserRoleId);

    public Task<long> InsertAsync(
        string userId, string passwordHash, UserAccountFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var id = Rows.Keys.Max() + 1;
        Hashes[id] = passwordHash;
        Rows[id] = new UserRow(id, userId, fields.UserName, fields.RoleId, "user", fields.Email, fields.Phone, fields.LangCode, fields.UserValidDt,
            fields.ValidFlag, true, 0, null, null, null, Start.AddMinutes(1));
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(
        long id, UserAccountFields fields, bool raiseTokenVersion, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = Rows[id];
        if (row.UpdatedAt != expectedUpdatedAt)
        {
            return Task.FromResult(false);
        }
        Rows[id] = row with
        {
            UserName = fields.UserName, RoleId = fields.RoleId, Email = fields.Email, Phone = fields.Phone, LangCode = fields.LangCode,
            UserValidDt = fields.UserValidDt, ValidFlag = fields.ValidFlag, UpdatedAt = row.UpdatedAt.AddMinutes(1),
        };
        if (raiseTokenVersion)
        {
            TokenVersionRaised.Enqueue(id);
        }
        return Task.FromResult(true);
    }

    public Task UnlockAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Rows[id] = Rows[id] with { FailedCount = 0, LockedUntil = null };
        return Task.CompletedTask;
    }

    public Task ResetPasswordAsync(
        long id, string passwordHash, DateTimeOffset now, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Hashes[id] = passwordHash;
        Rows[id] = Rows[id] with { MustChangePassword = true };
        TokenVersionRaised.Enqueue(id);
        return Task.CompletedTask;
    }

    public Task RaiseTokenVersionAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        TokenVersionRaised.Enqueue(id);
        return Task.CompletedTask;
    }
}
