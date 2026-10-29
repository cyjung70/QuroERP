using System.Data;
using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Domain.Bc.Roles;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Roles;

/// <summary>Roles and role menu permissions (BC-UC-005) on real PostgreSQL. Every test rolls back or cleans up.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class RoleRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    [DatabaseFact, Trait("Id", "BC-UT-211")]
    public async Task InUse_LeavesOutRolesNotInUse_AndFollowsSortOrder()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var (first, retired) = await AddRolesAsync(uow.Transaction, "zz_first", "zz_retired");
        var repository = db.Get<IRoleRepository>();

        var roles = await repository.FindInUseAsync(uow.Transaction, None);

        roles[0].RoleCode.Should().Be("zz_first");
        roles.Should().Contain(r => r.RoleCode == "admin").And.NotContain(r => r.RoleCode == "zz_retired");
        (await repository.FindInUseByIdAsync(first, uow.Transaction, None)).Should().Be(new RoleDto(first, "zz_first"));
        (await repository.FindInUseByIdAsync(retired, uow.Transaction, None)).Should().BeNull();
        (await repository.FindIdByCodeAsync("zz_retired", uow.Transaction, None)).Should().Be(retired);
    }

    [DatabaseFact, Trait("Id", "BC-UC-005-IT-01")]
    public async Task MenuWrites_CheckUpdatedAt_AndUnassignedListLeavesOutTheRolesMenus()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var (role, _) = await AddRolesAsync(uow.Transaction, "zz_rm1", "zz_rm1_off");
        var (a, b) = (await AddMenuAsync(uow.Transaction, "ZA_1", null), await AddMenuAsync(uow.Transaction, "ZA_2", null));
        var repository = db.Get<IRoleRepository>();

        await repository.InsertMenuAsync(role, a, false, "t", uow.Transaction, None);
        var loaded = (await repository.FindMenusAsync(role, uow.Transaction, None)).Single();
        var stale = await repository.UpdateMenuAsync(role, a, true, loaded.UpdatedAt.AddSeconds(-1), "t", uow.Transaction, None);
        var saved = await repository.UpdateMenuAsync(role, a, true, loaded.UpdatedAt, "t", uow.Transaction, None);
        var unassigned = await repository.FindUnassignedMenusAsync(role, "ZA_", uow.Transaction, None);

        loaded.Should().BeEquivalentTo(new { MenuId = a, MenuCode = "ZA_1", FullAccess = false });
        stale.Should().BeFalse();
        saved.Should().BeTrue();
        (await repository.FindMenusAsync(role, uow.Transaction, None)).Single().FullAccess.Should().BeTrue();
        unassigned.Select(m => m.MenuId).Should().Equal(b);
        (await repository.FindExistingMenuIdsAsync([a, b, -1], uow.Transaction, None)).Should().BeEquivalentTo([a, b]);
        (await repository.DeleteMenuAsync(role, a, loaded.UpdatedAt.AddSeconds(-1), uow.Transaction, None)).Should().BeFalse();
        await repository.DeleteAllMenusAsync(role, uow.Transaction, None);
        (await repository.FindMenusAsync(role, uow.Transaction, None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "BC-UT-214")]
    public async Task Permissions_LeaveOutMenusAndRolesNotInUse()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var (role, retired) = await AddRolesAsync(uow.Transaction, "zz_perm", "zz_perm_off");
        var on = await AddMenuAsync(uow.Transaction, "ZP_ON", null);
        var off = await AddMenuAsync(uow.Transaction, "ZP_OFF", null, validFlag: "N");
        var repository = db.Get<IRoleRepository>();
        foreach (var (roleId, menuId) in new[] { (role, on), (role, off), (retired, on) })
        {
            await repository.InsertMenuAsync(roleId, menuId, true, "t", uow.Transaction, None);
        }

        (await repository.FindPermissionsAsync("zz_perm", uow.Transaction, None)).Should().Equal(new RoleMenuGrant("ZP_ON", true));
        (await repository.FindPermissionsAsync("zz_perm_off", uow.Transaction, None)).Should().BeEmpty();
        (await repository.FindFullAccessRoleIdsAsync(on, uow.Transaction, None)).Should().BeEquivalentTo([role]);
    }

    [DatabaseFact, Trait("Id", "BC-UC-005-T02")]
    public async Task Save_WhenARowFails_SavesNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        long role;
        long first;
        long second;
        await using (var setup = await db.Get<IUnitOfWorkFactory>().BeginAsync(None))
        {
            (role, _) = await AddRolesAsync(setup.Transaction, "zz_rb" + suffix, "zz_rb_off" + suffix);
            first = await AddMenuAsync(setup.Transaction, "R1" + suffix, null);
            second = await AddMenuAsync(setup.Transaction, "R2" + suffix, null);
            await setup.CommitAsync(None);
        }
        try
        {
            var service = new RoleService(db.Get<IRoleRepository>(), new MenuPermissionService(db.Get<IRoleRepository>(), db.Get<IUnitOfWorkFactory>()),
                db.Get<IUnitOfWorkFactory>());

            // The first row is written before the second fails as unknown
            var act = () => service.SaveMenusAsync(role, new SaveRoleMenusRequest(
                [new RoleMenuInsert(first, true)], [new RoleMenuUpdate(second, true, DateTimeOffset.UnixEpoch)], null), null, "t", None);

            await act.Should().ThrowAsync<ConcurrencyException>();
            (await service.GetMenusAsync(role, None)).Should().BeEmpty();
        }
        finally
        {
            await using var cleanup = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
            await cleanup.Transaction.Connection!.ExecuteAsync(
                """
                DELETE FROM sys_role_menu WHERE role_id = @role;
                DELETE FROM sys_menu WHERE id IN (@first, @second);
                DELETE FROM sys_role WHERE role_code LIKE 'zz_rb%' || @suffix;
                """,
                new { role, first, second, suffix }, cleanup.Transaction);
            await cleanup.CommitAsync(None);
        }
    }

    [DatabaseFact, Trait("Id", "BC-UT-216")]
    public async Task RoleMenuScreen_IsFoundByProgramId()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IRoleRepository>();
        var before = await repository.LockRoleMenuScreenAsync(uow.Transaction, None);
        var screen = await AddMenuAsync(uow.Transaction, "ZROLEMENU", RoleMenuRules.RoleMenuProgramId);

        (await repository.LockRoleMenuScreenAsync(uow.Transaction, None)).Should().Be(before ?? screen);
    }

    // Returns (in use, not in use) role ids
    private static async Task<(long InUse, long Retired)> AddRolesAsync(IDbTransaction transaction, string inUse, string retired)
    {
        var ids = (await transaction.Connection!.QueryAsync<long>(
            """
            INSERT INTO sys_role (role_code, sort_order, use_yn, created_by, updated_by)
            VALUES (@inUse, -100, true, 't', 't'), (@retired, -101, false, 't', 't')
            RETURNING id
            """,
            new { inUse, retired }, transaction)).ToList();
        return (ids[0], ids[1]);
    }

    private static Task<long> AddMenuAsync(IDbTransaction transaction, string code, string? programId, string validFlag = "Y") =>
        transaction.Connection!.ExecuteScalarAsync<long>(
            """
            INSERT INTO sys_menu (menu_code, name, type, program_id, module, valid_flag, created_by, updated_by)
            VALUES (@code, @code, 'P', @programId, 'bc', @validFlag, 't', 't')
            RETURNING id
            """,
            new { code, programId, validFlag }, transaction);
}
