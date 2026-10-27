using System.Data;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Application.Tests.Common.Auth;

internal sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
{
    public int Commits { get; private set; }

    public Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IUnitOfWork>(new FakeUnitOfWork(this));

    private sealed class FakeUnitOfWork(FakeUnitOfWorkFactory owner) : IUnitOfWork
    {
        public IDbTransaction Transaction => null!;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            owner.Commits++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FakeAppUserRepository : IAppUserRepository
{
    public List<AppUser> Users { get; } = [];

    /// <summary>User IDs locked with FOR UPDATE, in order.</summary>
    public List<string> Locked { get; } = [];

    /// <summary>Saved login states: (user id, failed count, locked until, lang code).</summary>
    public List<(long Id, int FailedCount, DateTimeOffset? LockedUntil, string? LangCode)> Saved { get; } = [];

    public List<AuthMenu> Menus { get; } = [];

    public AuthWarehouse? DefaultWarehouse { get; set; }

    public Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.UserId == userId));

    public Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.Id == id));

    public Task<AppUser?> LockByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Locked.Add(userId);
        return FindByUserIdAsync(userId, transaction, cancellationToken);
    }

    public Task<AppUser?> LockByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        FindByIdAsync(id, transaction, cancellationToken);

    public Task SaveLoginStateAsync(AppUser user, string? langCode, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Saved.Add((user.Id, user.FailedCount, user.LockedUntil, langCode));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuthMenu>> FindMenusAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuthMenu>>(Menus);

    public Task<AuthWarehouse?> FindDefaultWarehouseAsync(long appUserId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(DefaultWarehouse);
}

internal sealed class FakeAuthSettingRepository : IAuthSettingRepository
{
    public AuthSetting Setting { get; set; } = new();

    public Task<AuthSetting?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult<AuthSetting?>(Setting);
}

/// <summary>sys_auto_login_token in memory, keyed by hash.</summary>
internal sealed class FakeAutoLoginTokenRepository : IAutoLoginTokenRepository
{
    public Dictionary<string, AutoLoginTokenRow> Tokens { get; } = [];

    public Task<AutoLoginTokenRow?> FindByHashAsync(
        string tokenHash, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Tokens.GetValueOrDefault(tokenHash));

    public Task InsertAsync(
        long appUserId, string tokenHash, DateTimeOffset expiresAt, string? userAgent, DateTimeOffset now,
        IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Tokens.Add(tokenHash, new AutoLoginTokenRow(Tokens.Count + 1, appUserId, expiresAt, null, now));
        return Task.CompletedTask;
    }

    public Task RevokeAsync(long appUserId, string tokenHash, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (Tokens.TryGetValue(tokenHash, out var token) && token.AppUserId == appUserId && token.RevokedAt is null)
        {
            Tokens[tokenHash] = token with { RevokedAt = now };
        }
        return Task.CompletedTask;
    }

    public Task RevokeAllAsync(long appUserId, DateTimeOffset now, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var (hash, token) in Tokens.Where(t => t.Value.AppUserId == appUserId && t.Value.RevokedAt is null).ToList())
        {
            Tokens[hash] = token with { RevokedAt = now };
        }
        return Task.CompletedTask;
    }
}

internal sealed class FakeUserLoginLogRepository : IUserLoginLogRepository
{
    public List<UserLoginLog> Logs { get; } = [];

    public Task InsertAsync(UserLoginLog log, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        Logs.Add(log);
        return Task.CompletedTask;
    }
}

internal sealed class FakeApiClientRepository : IApiClientRepository
{
    public Dictionary<string, ApiClient> ClientsByKeyHash { get; } = [];

    public Task<ApiClient?> FindByKeyHashAsync(string keyHash, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(ClientsByKeyHash.GetValueOrDefault(keyHash));
}

/// <summary>해시 대신 접두어를 붙인다. 계산 횟수를 센다.</summary>
internal sealed class FakePasswordHashService : IPasswordHashService
{
    public int HashCalls { get; private set; }

    public string Hash(string password)
    {
        HashCalls++;
        return "hash:" + password;
    }

    public bool Verify(string passwordHash, string password) => passwordHash == "hash:" + password;
}

internal sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public AccessToken Issue(AppUser user, bool mustChangePassword, DateTimeOffset now) =>
        new($"token-{user.Id}{(mustChangePassword ? "-pwd" : "")}", now.AddHours(24));
}
