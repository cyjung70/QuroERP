using System.Data;
using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Users;
using QuroErp.Application.Common.Persistence;
using QuroErp.Domain.Bc.Users;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Users;

/// <summary>User management reads and writes (BC-UC-004) on real PostgreSQL. Every test rolls back.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class UserRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "BC-UC-004-IT-01")]
    public async Task Insert_SetsMustChangePassword_AndNoPasswordExpiryDate()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserRepository>();
        var roleId = await RoleIdAsync(uow.Transaction, "admin");

        var id = await repository.InsertAsync("usr.new", "hash-1", Fields(roleId), "t", uow.Transaction, None);
        var row = (await repository.FindAsync(id, forUpdate: true, uow.Transaction, None))!;
        var expiry = await uow.Transaction.Connection!.ExecuteScalarAsync<DateTime?>(
            "SELECT password_valid_dt FROM sys_app_user WHERE id = @id", new { id }, uow.Transaction);

        row.Should().BeEquivalentTo(new
        {
            UserId = "usr.new",
            UserName = "New",
            RoleId = roleId,
            RoleCode = "admin",
            LangCode = "ko",
            ValidFlag = "Y",
            MustChangePassword = true,
            FailedCount = 0,
        });
        expiry.Should().BeNull();
        (await repository.FindPasswordHashAsync(id, uow.Transaction, None)).Should().Be("hash-1");
        (await repository.UserIdExistsAsync("usr.new", uow.Transaction, None)).Should().BeTrue();
    }

    [DatabaseFact, Trait("Id", "BC-UC-004-IT-02")]
    public async Task Search_TreatsLikeCharactersLiterally_AndPagesInUserIdOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserRepository>();
        var roleId = await RoleIdAsync(uow.Transaction, "admin");
        foreach (var userId in new[] { "usr_b", "usr_a", "usrxc" })
        {
            await repository.InsertAsync(userId, "h", Fields(roleId), "t", uow.Transaction, None);
        }

        var (page, total) = await repository.SearchAsync(new UserSearch("USR_", null, null, null, 1, 1), uow.Transaction, None);

        total.Should().Be(2);
        page.Select(u => u.UserId).Should().Equal("usr_b");
    }

    [DatabaseFact, Trait("Id", "BC-UC-004-IT-03")]
    public async Task Adjacent_FollowsUserIdOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserRepository>();
        var roleId = await RoleIdAsync(uow.Transaction, "admin");
        var first = await repository.InsertAsync("zz.adj1", "h", Fields(roleId), "t", uow.Transaction, None);
        var second = await repository.InsertAsync("zz.adj2", "h", Fields(roleId), "t", uow.Transaction, None);

        (await repository.FindAdjacentAsync(first, forward: true, uow.Transaction, None))!.Id.Should().Be(second);
        (await repository.FindAdjacentAsync(second, forward: false, uow.Transaction, None))!.Id.Should().Be(first);
    }

    [DatabaseFact, Trait("Id", "BC-UC-004-IT-04")]
    public async Task Update_ChecksUpdatedAt_AndRaisesTheTokenVersionOnlyWhenAsked()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserRepository>();
        var roleId = await RoleIdAsync(uow.Transaction, "admin");
        var id = await repository.InsertAsync("usr.upd", "h", Fields(roleId), "t", uow.Transaction, None);
        var before = (await repository.FindAsync(id, forUpdate: true, uow.Transaction, None))!;
        var version = await TokenVersionAsync(uow.Transaction, id);

        var stale = await repository.UpdateAsync(id, Fields(roleId, "N"), true, before.UpdatedAt.AddSeconds(-1), "t", uow.Transaction, None);
        var saved = await repository.UpdateAsync(id, Fields(roleId, "N"), true, before.UpdatedAt, "t", uow.Transaction, None);
        var after = (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None))!;

        stale.Should().BeFalse();
        saved.Should().BeTrue();
        after.ValidFlag.Should().Be("N");
        (await TokenVersionAsync(uow.Transaction, id)).Should().Be(version + 1);
    }

    [DatabaseFact, Trait("Id", "BC-UC-004-IT-05")]
    public async Task UnlockResetAndRevoke_WriteTheirColumns()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserRepository>();
        var roleId = await RoleIdAsync(uow.Transaction, "admin");
        var id = await repository.InsertAsync("usr.lock", "h", Fields(roleId), "t", uow.Transaction, None);
        await uow.Transaction.Connection!.ExecuteAsync(
            "UPDATE sys_app_user SET failed_count = 5, locked_until = now() + interval '1 hour' WHERE id = @id", new { id }, uow.Transaction);
        var version = await TokenVersionAsync(uow.Transaction, id);

        await repository.UnlockAsync(id, uow.Transaction, None);
        var unlocked = (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None))!;
        await repository.ResetPasswordAsync(id, "hash-2", DateTimeOffset.UtcNow, "t", uow.Transaction, None);
        await repository.RaiseTokenVersionAsync(id, uow.Transaction, None);

        unlocked.FailedCount.Should().Be(0);
        unlocked.LockedUntil.Should().BeNull();
        (await repository.FindPasswordHashAsync(id, uow.Transaction, None)).Should().Be("hash-2");
        (await repository.FindAsync(id, forUpdate: false, uow.Transaction, None))!.MustChangePassword.Should().BeTrue();
        (await TokenVersionAsync(uow.Transaction, id)).Should().Be(version + 2);
    }

    [DatabaseFact, Trait("Id", "BC-UT-122")]
    public async Task RoleInUse_FollowsSysRole()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IUserRepository>();

        (await repository.IsRoleInUseAsync(await RoleIdAsync(uow.Transaction, "admin"), uow.Transaction, None)).Should().BeTrue();
        (await repository.IsRoleInUseAsync(-1, uow.Transaction, None)).Should().BeFalse();
    }

    private static UserAccountFields Fields(long roleId, string validFlag = "Y") => new("New", roleId, null, null, "ko", null, validFlag);

    private static Task<long> RoleIdAsync(IDbTransaction transaction, string roleCode) =>
        transaction.Connection!.ExecuteScalarAsync<long>("SELECT id FROM sys_role WHERE role_code = @roleCode", new { roleCode }, transaction);

    private static Task<int> TokenVersionAsync(IDbTransaction transaction, long id) =>
        transaction.Connection!.ExecuteScalarAsync<int>("SELECT token_version FROM sys_app_user WHERE id = @id", new { id }, transaction);
}
