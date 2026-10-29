using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Common;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Domain.Bc.Roles;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Roles;

/// <summary>Role menu permissions (BC-UC-005, BC-BR-211~216) with fake storage.</summary>
public class RoleServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeRoleRepository _roles = new();
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly MenuPermissionService _permissions;
    private readonly RoleService _service;

    public RoleServiceTests()
    {
        _permissions = new MenuPermissionService(_roles, _uow);
        _service = new RoleService(_roles, _permissions, _uow);
    }

    [Theory, Trait("Id", "BC-UT-211")]
    [InlineData(3L)]
    [InlineData(99L)]
    public async Task RoleNotInUseOrMissing_IsBadRequestBcE211(long roleId)
    {
        var act = () => _service.GetMenusAsync(roleId, None);

        (await act.Should().ThrowAsync<BadRequestException>()).Which.Code.Should().Be(RoleMenuRules.NoRoleCode);
    }

    [Fact, Trait("Id", "BC-UC-005-T01")]
    public async Task Save_AddsChangesAndDeletes_ThenCommitsOnce()
    {
        _roles.Rows[(2, 101)] = (false, Read);
        _roles.Rows[(2, 102)] = (true, Read);

        var menus = await _service.SaveMenusAsync(2, new SaveRoleMenusRequest(
            [new RoleMenuInsert(100, false)], [new RoleMenuUpdate(101, true, Read)], [new RoleMenuDelete(102, Read)]), "admin", "kim", None);

        menus.Select(m => (m.MenuId, m.FullAccess)).Should().Equal((100L, false), (101L, true));
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-212")]
    public async Task Save_AMenuDeletedInTheSameSave_CanBeAddedAgain()
    {
        _roles.Rows[(2, 101)] = (false, Read);

        var menus = await _service.SaveMenusAsync(2, new SaveRoleMenusRequest(
            [new RoleMenuInsert(101, true)], null, [new RoleMenuDelete(101, Read)]), "admin", "kim", None);

        menus.Should().ContainSingle().Which.FullAccess.Should().BeTrue();
    }

    [Fact, Trait("Id", "BC-UC-005-T02")]
    public async Task Save_WithADuplicateRow_WritesAndCommitsNothing()
    {
        _roles.Rows[(2, 101)] = (false, Read);

        var act = () => _service.SaveMenusAsync(2, new SaveRoleMenusRequest(
            [new RoleMenuInsert(102, true), new RoleMenuInsert(101, true)], null, null), "admin", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(RoleMenuRules.DuplicateMenuCode);
        _roles.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-005-T02")]
    public async Task Save_WithAStaleRow_Is409_AndCommitsNothing()
    {
        _roles.Rows[(2, 101)] = (false, Read);

        var act = () => _service.SaveMenusAsync(2, new SaveRoleMenusRequest(
            null, [new RoleMenuUpdate(101, true, Read.AddSeconds(-1))], null), "admin", "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-005-T02")]
    public async Task Save_WithAnUnknownMenu_Is404()
    {
        var act = () => _service.SaveMenusAsync(2, new SaveRoleMenusRequest([new RoleMenuInsert(999, true)], null, null), "admin", "kim", None);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Args.Should().Equal(999L);
    }

    [Fact, Trait("Id", "BC-UT-216")]
    public async Task TakingFullAccessToTheRoleMenuScreenFromMyRole_IsBcE216_AndNotCommitted()
    {
        _roles.Rows[(1, 100)] = (true, Read);
        _roles.Rows[(2, 100)] = (true, Read);

        var act = () => _service.SaveMenusAsync(1, new SaveRoleMenusRequest(
            null, [new RoleMenuUpdate(100, false, Read)], null), "admin", "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(RoleMenuRules.LastAdminCode);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-216")]
    public async Task DeletingAllMenusOfTheLastManagingRole_IsBcE216()
    {
        _roles.Rows[(2, 100)] = (true, Read);

        var act = () => _service.DeleteMenusAsync(2, "admin", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(RoleMenuRules.LastAdminCode);
    }

    [Fact, Trait("Id", "BC-UT-216")]
    public async Task WithoutTheRoleMenuScreenLoaded_ThereIsNothingToKeep()
    {
        _roles.ScreenLoaded = false;
        _roles.Rows[(2, 100)] = (true, Read);

        await _service.DeleteMenusAsync(2, "user", None);

        _roles.Rows.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-215")]
    public async Task Saving_ClearsTheCachedPermissionsOfThatRole()
    {
        _roles.Rows[(2, 101)] = (false, Read);
        (await _permissions.GetAccessAsync("user", "ITEM", None)).Should().Be(MenuAccess.Read);

        await _service.SaveMenusAsync(2, new SaveRoleMenusRequest(null, [new RoleMenuUpdate(101, true, Read)], null), "admin", "kim", None);

        (await _permissions.GetAccessAsync("user", "ITEM", None)).Should().Be(MenuAccess.Full);
    }

    [Fact, Trait("Id", "BC-UT-215")]
    public async Task Permissions_AreReadOncePerRole_UntilCleared()
    {
        _roles.Rows[(2, 101)] = (true, Read);

        (await _permissions.GetAccessAsync("user", "ITEM", None)).Should().Be(MenuAccess.Full);
        (await _permissions.GetAccessAsync("user", "USER", None)).Should().Be(MenuAccess.None);
        _roles.PermissionReads.Should().Be(1);
        _permissions.Invalidate("user");
        await _permissions.GetAccessAsync("user", "ITEM", None);
        _roles.PermissionReads.Should().Be(2);
    }

    /// <summary>Roles admin (1), user (2), retired (3, not in use); menus 100 (role menu screen), 101 ITEM, 102 USER.</summary>
    private sealed class FakeRoleRepository : IRoleRepository
    {
        private static readonly Dictionary<long, string> Codes = new() { [100] = "ROLEMENU", [101] = "ITEM", [102] = "USER" };
        private static readonly Dictionary<long, string> RoleCodes = new() { [1] = "admin", [2] = "user", [3] = "retired" };

        public Dictionary<(long RoleId, long MenuId), (bool FullAccess, DateTimeOffset UpdatedAt)> Rows { get; } = [];

        public bool ScreenLoaded { get; set; } = true;

        public int Writes { get; private set; }

        public int PermissionReads { get; private set; }

        public Task<IReadOnlyList<RoleDto>> FindInUseAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleDto>>([new(1, "admin"), new(2, "user")]);

        public Task<RoleDto?> FindInUseByIdAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(roleId is 1 or 2 ? new RoleDto(roleId, RoleCodes[roleId]) : null);

        public Task<long?> FindIdByCodeAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult(RoleCodes.Where(r => r.Value == roleCode).Select(r => (long?)r.Key).FirstOrDefault());

        public Task<IReadOnlyList<RoleMenuDto>> FindMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleMenuDto>>(Rows.Where(r => r.Key.RoleId == roleId).OrderBy(r => r.Key.MenuId)
                .Select(r => new RoleMenuDto(r.Key.MenuId, Codes[r.Key.MenuId], null, "P", "bc", r.Value.FullAccess, r.Value.UpdatedAt)).ToList());

        public Task<IReadOnlyList<MenuOptionDto>> FindUnassignedMenusAsync(
            long roleId, string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<long>> FindExistingMenuIdsAsync(
            IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(menuIds.Where(Codes.ContainsKey).ToHashSet());

        public Task<long?> LockRoleMenuScreenAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<long?>(ScreenLoaded ? 100 : null);

        public Task<IReadOnlySet<long>> FindFullAccessRoleIdsAsync(long menuId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(Rows.Where(r => r.Key.MenuId == menuId && r.Value.FullAccess && r.Key.RoleId != 3)
                .Select(r => r.Key.RoleId).ToHashSet());

        public Task InsertMenuAsync(
            long roleId, long menuId, bool fullAccess, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            Rows.Add((roleId, menuId), (fullAccess, Read.AddMinutes(1)));
            return Task.CompletedTask;
        }

        public Task<bool> UpdateMenuAsync(
            long roleId, long menuId, bool fullAccess, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            if (!Rows.TryGetValue((roleId, menuId), out var row) || row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            Rows[(roleId, menuId)] = (fullAccess, row.UpdatedAt.AddMinutes(1));
            return Task.FromResult(true);
        }

        public Task<bool> DeleteMenuAsync(
            long roleId, long menuId, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.FromResult(Rows.TryGetValue((roleId, menuId), out var row) && row.UpdatedAt == expectedUpdatedAt
                && Rows.Remove((roleId, menuId)));
        }

        public Task DeleteAllMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            foreach (var key in Rows.Keys.Where(k => k.RoleId == roleId).ToList())
            {
                Rows.Remove(key);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RoleMenuGrant>> FindPermissionsAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            PermissionReads++;
            var roleId = RoleCodes.Single(r => r.Value == roleCode).Key;
            return Task.FromResult<IReadOnlyList<RoleMenuGrant>>(Rows.Where(r => r.Key.RoleId == roleId)
                .Select(r => new RoleMenuGrant(Codes[r.Key.MenuId], r.Value.FullAccess)).ToList());
        }
    }
}
