using Dapper;
using FluentAssertions;
using QuroERP.Database;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Application.Common.Auth;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Common.Auth;
using QuroErp.Domain.Common.Auth;
using QuroErp.Infrastructure.Common.Persistence;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Common.Auth;

/// <summary>Login state, auth setting, auto-login token, menu and warehouse queries on real PostgreSQL (TASK-BC-001).</summary>
[Collection(AuthSchemaDefinition.Name)]
public class LoginRepositoryTests(AuthSchemaFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact, Trait("Id", "BC-001-IT-01")]
    public async Task GetAsync_SeededRow_HasTheLoginMdDefaults()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);

        var setting = await db.Get<IAuthSettingRepository>().GetAsync(uow.Transaction, CancellationToken.None);

        setting.Should().BeEquivalentTo(new AuthSetting());
    }

    [DatabaseFact, Trait("Id", "BC-001-IT-02")]
    public async Task SaveLoginStateAsync_LockAndLogin_RoundTripsThroughLockByUserId_AndKeepsUpdatedAt()
    {
        var id = await CreateUserAsync("login1");
        var users = db.Get<IAppUserRepository>();
        var updatedAt = await UpdatedAtAsync(id);

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            var user = (await users.LockByUserIdAsync("login1", uow.Transaction, CancellationToken.None))!;
            for (var i = 0; i < 5; i++)
            {
                user.RecordFailure(Now, new AuthSetting());
            }
            await users.SaveLoginStateAsync(user, "en", uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            var user = (await users.LockByIdAsync(id, uow.Transaction, CancellationToken.None))!;
            user.FailedCount.Should().Be(5);
            user.LockedUntil.Should().Be(Now.AddMinutes(60));
            user.LangCode.Should().Be("en");

            user.ReleaseExpiredLock(Now.AddHours(2));
            user.RecordSuccess(Now.AddHours(2), "10.0.0.9");
            await users.SaveLoginStateAsync(user, null, uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var check = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var saved = (await users.FindByIdAsync(id, check.Transaction, CancellationToken.None))!;
        saved.Should().BeEquivalentTo(new
        {
            FailedCount = 0,
            LockedUntil = (DateTimeOffset?)null,
            LastLoginAt = (DateTimeOffset?)Now.AddHours(2),
            LastLoginIp = "10.0.0.9",
            LangCode = "en",
        });
        (await UpdatedAtAsync(id)).Should().Be(updatedAt);
    }

    private async Task<DateTimeOffset> UpdatedAtAsync(long id)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        return (await db.Get<IUserProfileRepository>().FindAsync(id, forUpdate: false, uow.Transaction, CancellationToken.None))!.UpdatedAt;
    }

    [DatabaseFact, Trait("Id", "BC-001-IT-03")]
    public async Task AutoLoginTokens_InsertFindRevokeAndRevokeAll()
    {
        var id = await CreateUserAsync("login2");
        var tokens = db.Get<IAutoLoginTokenRepository>();
        var first = AutoLoginToken.Hash(AutoLoginToken.Create());
        var second = AutoLoginToken.Hash(AutoLoginToken.Create());
        var third = AutoLoginToken.Hash(AutoLoginToken.Create());

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            foreach (var hash in new[] { first, second, third })
            {
                await tokens.InsertAsync(id, hash, Now.AddDays(14), "Mozilla/5.0", Now, uow.Transaction, CancellationToken.None);
            }
            await tokens.RevokeAsync(id, first, Now.AddMinutes(1), uow.Transaction, CancellationToken.None);
            // Another user's ID does not revoke the token
            await tokens.RevokeAsync(id + 1000, second, Now.AddMinutes(1), uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            var found = await tokens.FindByHashAsync(first, true, uow.Transaction, CancellationToken.None);
            found.Should().Be(new AutoLoginTokenRow(found!.Id, id, Now.AddDays(14), Now.AddMinutes(1), Now));
            (await tokens.FindByHashAsync(second, false, uow.Transaction, CancellationToken.None))!.RevokedAt.Should().BeNull();

            await tokens.RevokeAllAsync(id, Now.AddMinutes(2), uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None);
        var revoked = (await connection.QueryAsync<DateTime>(
            "SELECT revoked_at FROM sys_auto_login_token WHERE app_user_id = @Id ORDER BY id", new { Id = id })).ToList();
        revoked.Should().Equal(Now.AddMinutes(1).UtcDateTime, Now.AddMinutes(2).UtcDateTime, Now.AddMinutes(2).UtcDateTime);
    }

    [DatabaseFact, Trait("Id", "BC-001-IT-04")]
    public async Task InsertAsync_LoginLog_StoresEventResultAndUserAgent()
    {
        var id = await CreateUserAsync("login3");

        await using (var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None))
        {
            await db.Get<IUserLoginLogRepository>().InsertAsync(
                new UserLoginLog(Now, id, "login3", "10.0.0.1", false, LoginEvents.Locked, AuthCodes.Locked, "Mozilla/5.0"),
                uow.Transaction, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None);
        var row = await connection.QuerySingleAsync<(string EventType, string ResultCode, string UserAgent)>(
            "SELECT event_type, result_code, user_agent FROM sys_user_login_log WHERE app_user_id = @Id", new { Id = id });
        row.Should().Be((LoginEvents.Locked, AuthCodes.Locked, "Mozilla/5.0"));
    }

    [DatabaseFact, Trait("Id", "BC-001-IT-05")]
    public async Task FindMenusAndDefaultWarehouse_ReturnRoleMenusAndCompanyDefaultWarehouse()
    {
        var id = await CreateUserAsync("login4");
        await using (var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO sys_menu (menu_code, name, program_id, module, valid_flag, created_by, updated_by)
                VALUES ('T-ROOT', 'Root', NULL, 'BC', 'Y', 'test', 'test');
                INSERT INTO sys_menu (menu_code, name, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
                SELECT 'T-CHILD', 'Child', 'bc-child', 'BC', id, 'Y', 'test', 'test' FROM sys_menu WHERE menu_code = 'T-ROOT';
                INSERT INTO sys_menu (menu_code, name, valid_flag, created_by, updated_by) VALUES ('T-OFF', 'Off', 'N', 'test', 'test');
                INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
                SELECT u.role_id, m.id, 'Y', 'test', 'test' FROM sys_app_user u, sys_menu m
                WHERE u.id = @Id AND m.menu_code IN ('T-ROOT', 'T-CHILD', 'T-OFF');
                """,
                new { Id = id });
        }

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var users = db.Get<IAppUserRepository>();
        var menus = await users.FindMenusAsync(id, uow.Transaction, CancellationToken.None);
        var warehouse = await users.FindDefaultWarehouseAsync(id, uow.Transaction, CancellationToken.None);

        menus.Where(m => m.MenuCode.StartsWith("T-", StringComparison.Ordinal)).Should().Equal(
            new AuthMenu("T-CHILD", "Child", "bc-child", "BC", "T-ROOT", "Y", null),
            new AuthMenu("T-ROOT", "Root", null, "BC", null, "Y", null));
        var isDefault = await uow.Transaction.Connection!.QuerySingleAsync<long>(
            "SELECT id FROM bc_warehouse WHERE is_default", transaction: uow.Transaction);
        warehouse!.Id.Should().Be(isDefault);
    }

    [DatabaseFact, Trait("Id", "CORE-037-IT-01")]
    public async Task FindMenus_AddsUnassignedParentGroups_AndDropsMenusUnderAGroupNotInUse()
    {
        var id = await CreateUserAsync("shell1");
        await using (var connection = await db.Get<IDbConnectionFactory>().OpenAsync(CancellationToken.None))
        {
            // Group T2-G holds subheading T2-S; group T2-X is not in use. Only P, U menus are assigned, plus nothing above them
            await connection.ExecuteAsync(
                """
                INSERT INTO sys_menu (menu_code, name, type, module, valid_flag, created_by, updated_by) VALUES
                    ('T2-G', 'Group', 'M', 'bc', 'Y', 'test', 'test'),
                    ('T2-X', 'Off group', 'M', 'bc', 'N', 'test', 'test');
                INSERT INTO sys_menu (menu_code, name, type, module, parent_menu_id, valid_flag, created_by, updated_by)
                SELECT 'T2-S', 'Sub', 'M', 'bc', id, 'Y', 'test', 'test' FROM sys_menu WHERE menu_code = 'T2-G';
                INSERT INTO sys_menu (menu_code, name, type, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
                SELECT v.code, v.code, v.type, v.path, 'bc', p.id, 'Y', 'test', 'test'
                FROM (VALUES ('T2-P1', 'P', '/bc/p1', 'T2-S'), ('T2-P2', 'P', '/bc/p2', 'T2-G'), ('T2-U', 'U', '/bc/u', 'T2-G'),
                             ('T2-P3', 'P', '/bc/p3', 'T2-X')) AS v (code, type, path, parent)
                JOIN sys_menu p ON p.menu_code = v.parent;
                INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
                SELECT u.role_id, m.id, 'Y', 'test', 'test' FROM sys_app_user u, sys_menu m
                WHERE u.id = @Id AND m.menu_code IN ('T2-P1', 'T2-P2', 'T2-U', 'T2-P3');
                """,
                new { Id = id });
        }

        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var menus = await db.Get<IAppUserRepository>().FindMenusAsync(id, uow.Transaction, CancellationToken.None);

        menus.Where(m => m.MenuCode.StartsWith("T2-", StringComparison.Ordinal)).Should().Equal(
            new AuthMenu("T2-G", "Group", null, "bc", null, null, "M"),
            new AuthMenu("T2-P1", "T2-P1", "/bc/p1", "bc", "T2-S", "Y", "P"),
            new AuthMenu("T2-P2", "T2-P2", "/bc/p2", "bc", "T2-G", "Y", "P"),
            new AuthMenu("T2-S", "Sub", null, "bc", "T2-G", null, "M"),
            new AuthMenu("T2-U", "T2-U", "/bc/u", "bc", "T2-G", "Y", "U"));
    }

    [DatabaseFact, Trait("Id", "CORE-037-IT-02")]
    public async Task FindCompanyName_ReturnsTheCompanyRow()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        var expected = await uow.Transaction.Connection!.QuerySingleOrDefaultAsync<string?>(
            "SELECT name FROM bc_company ORDER BY id LIMIT 1", transaction: uow.Transaction);

        var name = await db.Get<IAppUserRepository>().FindCompanyNameAsync(uow.Transaction, CancellationToken.None);

        name.Should().Be(expected);
    }

    private async Task<long> CreateUserAsync(string userId)
    {
        await AdminAccount.CreateOrResetAsync(db.ConnectionString, userId, "Pass-1", CancellationToken.None);
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(CancellationToken.None);
        return (await db.Get<IAppUserRepository>().FindByUserIdAsync(userId, uow.Transaction, CancellationToken.None))!.Id;
    }
}
