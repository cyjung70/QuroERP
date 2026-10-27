using Dapper;
using FluentAssertions;
using QuroERP.Database;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Common.Auth;
using QuroErp.Infrastructure.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.Auth;

/// <summary>인증 Repository·관리자 생성 통합 테스트 (TASK-CORE-003, 실제 PostgreSQL).</summary>
[Collection(AuthSchemaDefinition.Name)]
public class AuthRepositoryTests(AuthSchemaFixture db)
{
    [DatabaseFact, Trait("Id", "CORE-003-IT-01")]
    public async Task CreateOrResetAsync_NewAdmin_StoresOnlyPbkdf2HashAndForcesPasswordChange()
    {
        var created = await AdminAccount.CreateOrResetAsync(db.ConnectionString, "admin1", "Init-Pass-1", CancellationToken.None);

        var user = await FindAsync("admin1");
        created.Should().BeTrue();
        user!.PasswordHash.Should().NotContain("Init-Pass-1");
        Convert.FromBase64String(user.PasswordHash)[0].Should().Be(0x01, "Identity V3 형식(PBKDF2)이다");
        db.Get<IPasswordHashService>().Verify(user.PasswordHash, "Init-Pass-1").Should().BeTrue();
        user.MustChangePassword.Should().BeTrue();
        user.RoleCode.Should().Be(AdminAccount.RoleCode);
        user.CanLogin(DateOnly.FromDateTime(DateTime.Today)).Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "CORE-003-IT-02")]
    public async Task CreateOrResetAsync_ExistingUser_ResetsPassword()
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, "admin2", "First-Pass-1", CancellationToken.None);

        var created = await AdminAccount.CreateOrResetAsync(db.ConnectionString, "admin2", "Second-Pass-2", CancellationToken.None);

        var user = await FindAsync("admin2");
        created.Should().BeFalse();
        var passwords = db.Get<IPasswordHashService>();
        passwords.Verify(user!.PasswordHash, "Second-Pass-2").Should().BeTrue();
        passwords.Verify(user.PasswordHash, "First-Pass-1").Should().BeFalse();
    }

    [DatabaseFact, Trait("Id", "CORE-003-IT-03")]
    public async Task FindByIdAsync_ExistingUser_MapsColumns()
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, "admin3", "Pass-3", CancellationToken.None);
        var id = (await FindAsync("admin3"))!.Id;

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var user = await db.Get<IAppUserRepository>().FindByIdAsync(id, uow.Transaction, CancellationToken.None);

        user.Should().BeEquivalentTo(new { Id = id, UserId = "admin3", UserName = "admin3", ValidFlag = "Y" });
    }

    [DatabaseFact, Trait("Id", "CORE-003-IT-04")]
    public async Task InsertAsync_SuccessAndFailureAtSameTime_StoresBoth()
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, "admin4", "Pass-4", CancellationToken.None);
        var userId = (await FindAsync("admin4"))!.Id;
        var logDt = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            var logs = db.Get<IUserLoginLogRepository>();
            await logs.InsertAsync(new UserLoginLog(logDt, userId, "admin4", "10.0.0.1", true, LoginEvents.LoginOk, null, "Mozilla/5.0"), uow.Transaction, CancellationToken.None);
            await logs.InsertAsync(new UserLoginLog(logDt, null, "ghost", "", false, LoginEvents.LoginFail, AuthCodes.InvalidCredentials, ""), uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None);
        var rows = (await connection.QueryAsync<(long? AppUserId, string LoginUserid, string? IpAddr, bool Success, DateTime LogDt)>(
            "SELECT app_user_id, login_userid, ip_addr, success, log_dt FROM sys_user_login_log WHERE login_userid IN ('admin4', 'ghost') ORDER BY id")).ToList();
        rows.Should().Equal(
            (userId, "admin4", "10.0.0.1", true, logDt.UtcDateTime),
            ((long?)null, "ghost", (string?)null, false, logDt.UtcDateTime));
    }

    [DatabaseFact, Trait("Id", "CORE-003-IT-05")]
    public async Task FindByKeyHashAsync_RevokedClient_ReturnsClientWithRevokedAt()
    {
        var revokedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO sys_api_client (client_code, key_hash, role_id, revoked_at, created_by, updated_by)
                SELECT @ClientCode, @KeyHash, id, @RevokedAt, 'test', 'test' FROM sys_role WHERE role_code = 'manager'
                """,
                new { ClientCode = "edi", KeyHash = ApiKeyHash.Compute("edi-key"), RevokedAt = revokedAt });
        }

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var client = await db.Get<IApiClientRepository>().FindByKeyHashAsync(ApiKeyHash.Compute("edi-key"), uow.Transaction, CancellationToken.None);

        client.Should().BeEquivalentTo(new { ClientCode = "edi", RoleCode = "manager", RevokedAt = (DateTimeOffset?)revokedAt });
    }

    private async Task<AppUser?> FindAsync(string userId)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        return await db.Get<IAppUserRepository>().FindByUserIdAsync(userId, uow.Transaction, CancellationToken.None);
    }
}
