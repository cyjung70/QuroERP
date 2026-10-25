using System.Data;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
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

    public Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.UserId == userId));

    public Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.Id == id));
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
    public AccessToken Issue(AppUser user, DateTimeOffset now) => new($"token-{user.Id}", now.AddHours(24));
}
