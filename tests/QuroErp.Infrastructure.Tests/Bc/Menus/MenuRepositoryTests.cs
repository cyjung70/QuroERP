using System.Data;
using Dapper;
using FluentAssertions;
using QuroErp.Application.Bc.Menus;
using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Common.Persistence;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Domain.Bc.Menus;
using QuroErp.Domain.Common;
using QuroErp.Infrastructure.Tests.Common.Auth;
using QuroErp.Infrastructure.Tests.Common.Persistence;

namespace QuroErp.Infrastructure.Tests.Bc.Menus;

/// <summary>Company menus (BC-UC-006) on real PostgreSQL. Repository tests roll back; service tests delete what they commit.</summary>
[Collection(AuthSchemaDefinition.Name)]
public class MenuRepositoryTests(AuthSchemaFixture db)
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly MenuFields Group = new("Group", "M", null, "bc", null, "Y");
    private static readonly MenuFields Program = new("Program", "P", "/bc/zm", "bc", null, "Y");

    private MenuService Service
    {
        get
        {
            var unitOfWork = db.Get<IUnitOfWorkFactory>();
            return new MenuService(
                db.Get<IMenuRepository>(), new CodeValidator(db.Get<ICodeRepository>(), unitOfWork),
                new MenuPermissionService(db.Get<IRoleRepository>(), unitOfWork), unitOfWork);
        }
    }

    [DatabaseFact, Trait("Id", "BC-UC-006-IT-01")]
    public async Task Writes_CheckUpdatedAt_AndSearchShowsTheParentCode()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IMenuRepository>();

        var parent = await repository.InsertAsync("ZM_GRP", Group, "t", uow.Transaction, None);
        var child = await repository.InsertAsync("ZM_PGM", Program, "t", uow.Transaction, None);
        await repository.SetParentAsync(child, parent, uow.Transaction, None);
        var locked = (await repository.LockAllAsync(uow.Transaction, None)).Single(m => m.Id == child);
        var stale = await repository.UpdateAsync(child, Program with { Name = "Stale" }, parent, locked.UpdatedAt.AddSeconds(-1), "t", uow.Transaction, None);
        var saved = await repository.UpdateAsync(child, Program with { Name = "Renamed", ValidFlag = "N" }, parent, locked.UpdatedAt, "t", uow.Transaction, None);
        var found = await repository.SearchAsync("ZM_", uow.Transaction, None);

        locked.Should().Be(new MenuRecord(child, "ZM_PGM", "P", parent, locked.UpdatedAt));
        stale.Should().BeFalse();
        saved.Should().BeTrue();
        found.Select(m => (m.MenuCode, m.Name, m.ParentMenuCode, m.ValidFlag)).Should().Equal(
            ("ZM_GRP", "Group", null, "Y"), ("ZM_PGM", "Renamed", "ZM_GRP", "N"));
    }

    [DatabaseFact, Trait("Id", "BC-UC-006-T03")]
    public async Task Search_TreatsLikeCharactersAsText()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IMenuRepository>();
        await repository.InsertAsync("ZQ_A", Program, "t", uow.Transaction, None);
        await repository.InsertAsync("ZQXB", Program, "t", uow.Transaction, None);

        (await repository.SearchAsync("ZQ_", uow.Transaction, None)).Select(m => m.MenuCode).Should().Equal("ZQ_A");
        (await repository.SearchAsync("ZQ%", uow.Transaction, None)).Should().BeEmpty();
    }

    [DatabaseFact, Trait("Id", "BC-UT-207")]
    public async Task Assigned_FindsMenusHeldByARole_AndDeleteRemovesAParentWithItsChildren()
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        var repository = db.Get<IMenuRepository>();
        var parent = await repository.InsertAsync("ZD_GRP", Group, "t", uow.Transaction, None);
        var child = await repository.InsertAsync("ZD_PGM", Program, "t", uow.Transaction, None);
        var held = await repository.InsertAsync("ZD_HELD", Program, "t", uow.Transaction, None);
        await repository.SetParentAsync(child, parent, uow.Transaction, None);
        await AssignToAdminAsync(uow.Transaction, held);

        var assigned = await repository.FindAssignedAsync([parent, child, held], uow.Transaction, None);
        await repository.DeleteAsync([parent, child], uow.Transaction, None);

        assigned.Should().BeEquivalentTo([held]);
        (await repository.SearchAsync("ZD_", uow.Transaction, None)).Select(m => m.MenuCode).Should().Equal("ZD_HELD");
    }

    [DatabaseFact, Trait("Id", "BC-UC-006-T01")]
    public async Task Save_AddsAGroupWithAChild_ThenChangesAndDeletesThem()
    {
        try
        {
            var added = await Service.SaveAsync(new SaveMenusRequest(
                [new MenuInsert("ZS_PGM", "Program", "P", "/bc/zs", "bc", "ZS_GRP", null), new MenuInsert("ZS_GRP", "Group", "M", null, "bc", null, null)],
                null, null), "t", None);
            var (group, program) = (added.Single(m => m.MenuCode == "ZS_GRP"), added.Single(m => m.MenuCode == "ZS_PGM"));

            var changed = await Service.SaveAsync(new SaveMenusRequest(
                null, [new MenuUpdate(program.Id, "Moved", "P", "/bc/zs", "bc", null, "N", program.UpdatedAt)], [new MenuDelete(group.Id, group.UpdatedAt)]),
                "t", None);

            program.ParentMenuCode.Should().Be("ZS_GRP");
            changed.Where(m => m.MenuCode.StartsWith("ZS_", StringComparison.Ordinal)).Select(m => (m.MenuCode, m.Name, m.ParentMenuCode, m.ValidFlag))
                .Should().Equal(("ZS_PGM", "Moved", null, "N"));
        }
        finally
        {
            await DeleteCommittedAsync("ZS_");
        }
    }

    [DatabaseFact, Trait("Id", "BC-UC-006-T02")]
    public async Task Save_WithAFailingRow_LeavesNothingBehind()
    {
        try
        {
            var act = () => Service.SaveAsync(new SaveMenusRequest(
                [new MenuInsert("ZR_OK", "Fine", "P", "/bc/zr", "bc", null, null), new MenuInsert("ZR_BAD", "Bad", "P", "/bc/zr2", "bc", "ZR_OK", null)],
                null, null), "t", None);

            var error = (await act.Should().ThrowAsync<DomainException>()).Which;
            error.Code.Should().Be(MenuRules.InvalidParentCode);
            error.Args.Should().Equal("ZR_BAD");
            (await Service.SearchAsync("ZR_", None)).Should().BeEmpty();
        }
        finally
        {
            await DeleteCommittedAsync("ZR_");
        }
    }

    private static Task<int> AssignToAdminAsync(IDbTransaction transaction, long menuId) =>
        transaction.Connection!.ExecuteAsync(
            """
            INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
            SELECT id, @menuId, 'N', 't', 't' FROM sys_role WHERE role_code = 'admin'
            """,
            new { menuId }, transaction);

    private async Task DeleteCommittedAsync(string prefix)
    {
        await using var uow = await db.Get<IUnitOfWorkFactory>().BeginAsync(None);
        await uow.Transaction.Connection!.ExecuteAsync(
            "UPDATE sys_menu SET parent_menu_id = NULL WHERE menu_code LIKE @Pattern", new { Pattern = prefix + "%" }, uow.Transaction);
        await uow.Transaction.Connection!.ExecuteAsync("DELETE FROM sys_menu WHERE menu_code LIKE @Pattern", new { Pattern = prefix + "%" }, uow.Transaction);
        await uow.CommitAsync(None);
    }
}
