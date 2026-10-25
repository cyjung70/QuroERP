using System.Collections.Concurrent;
using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Tests;

/// <summary>
/// Api 호스트를 메모리에서 띄운다. DB 대신 가짜 Repository를 쓰고, 비밀번호 해시·JWT·인증 미들웨어는 실제 구성을 쓴다.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string UserId = "kim";
    public const string Password = "Correct-Pass-1";
    public const string ApiKey = "test-api-key-0123456789";
    public const string RevokedApiKey = "revoked-api-key-0123456789";

    public ConcurrentQueue<UserLoginLog> LoginLogs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 연결은 열지 않는다 (Repository를 가짜로 바꾼다)
        builder.UseSetting("ConnectionStrings:QuroErp", "Host=localhost;Database=unused");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789-abcdefghij");

        builder.ConfigureTestServices(services =>
        {
            var user = new AppUser
            {
                Id = 7, UserId = UserId, UserName = "Kim", RoleCode = "admin", ValidFlag = "Y",
                PasswordHash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), Password),
            };
            services.AddSingleton<IUnitOfWorkFactory, FakeUnitOfWorkFactory>();
            services.AddSingleton<IAppUserRepository>(new FakeAppUserRepository(user));
            services.AddSingleton<IUserLoginLogRepository>(new FakeUserLoginLogRepository(LoginLogs));
            services.AddSingleton<IApiClientRepository>(new FakeApiClientRepository(new Dictionary<string, ApiClient>
            {
                [ApiKeyHash.Compute(ApiKey)] = new() { ClientCode = "edi", RoleCode = "manager" },
                [ApiKeyHash.Compute(RevokedApiKey)] = new() { ClientCode = "old", RoleCode = "manager", RevokedAt = DateTimeOffset.UnixEpoch },
            }));
        });
    }

    private sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IUnitOfWork>(new FakeUnitOfWork());

        private sealed class FakeUnitOfWork : IUnitOfWork
        {
            public IDbTransaction Transaction => null!;

            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAppUserRepository(AppUser user) : IAppUserRepository
    {
        public Task<AppUser?> FindByUserIdAsync(string userId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(userId == user.UserId ? user : null);

        public Task<AppUser?> FindByIdAsync(long id, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(id == user.Id ? user : null);
    }

    private sealed class FakeUserLoginLogRepository(ConcurrentQueue<UserLoginLog> logs) : IUserLoginLogRepository
    {
        public Task InsertAsync(UserLoginLog log, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            logs.Enqueue(log);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApiClientRepository(Dictionary<string, ApiClient> clients) : IApiClientRepository
    {
        public Task<ApiClient?> FindByKeyHashAsync(string keyHash, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(clients.GetValueOrDefault(keyHash));
    }
}
