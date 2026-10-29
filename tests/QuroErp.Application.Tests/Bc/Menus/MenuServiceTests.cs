using System.Data;
using FluentAssertions;
using QuroErp.Application.Bc.Menus;
using QuroErp.Application.Bc.Roles;
using QuroErp.Application.Common;
using QuroErp.Application.Common.Codes;
using QuroErp.Application.Tests.Common.Auth;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Domain.Bc.Menus;
using QuroErp.Domain.Common;

namespace QuroErp.Application.Tests.Bc.Menus;

/// <summary>Company menus (BC-UC-006, BC-BR-201~207) with fake storage.</summary>
public class MenuServiceTests
{
    private static readonly DateTimeOffset Read = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeMenuRepository _menus = new();
    private readonly FakeGrants _grants = new();
    private readonly FakeUnitOfWorkFactory _uow = new();
    private readonly MenuPermissionService _permissions;
    private readonly MenuService _service;

    public MenuServiceTests()
    {
        _permissions = new MenuPermissionService(_grants, _uow);
        _service = new MenuService(_menus, new FakeCodeValidator(), _permissions, _uow);
        _menus.Add(1, "BASE", "M", null);
        _menus.Add(2, "ITEM", "P", 1);
        _menus.Add(3, "USER", "P", 1);
    }

    [Fact, Trait("Id", "BC-UC-006-T01")]
    public async Task Save_AddsChangesAndDeletes_ThenCommitsOnce()
    {
        var menus = await _service.SaveAsync(new SaveMenusRequest(
            [new MenuInsert("master", "Master", "M", null, "bc", "BASE", null), new MenuInsert("whs", "Warehouse", "P", "/bc/whs", "bc", "MASTER", null)],
            [new MenuUpdate(2, "Items", "P", "/bc/item", "bc", "MASTER", "N", Read)],
            [new MenuDelete(3, Read)]), "kim", None);

        menus.Select(m => (m.MenuCode, m.ParentMenuCode)).Should().Equal(("BASE", null), ("ITEM", "MASTER"), ("MASTER", "BASE"), ("WHS", "MASTER"));
        menus.Single(m => m.MenuCode == "ITEM").ValidFlag.Should().Be("N");
        _uow.Commits.Should().Be(1);
    }

    [Fact, Trait("Id", "BC-UT-201")]
    public async Task Save_ADeletedCode_CanBeAddedAgain()
    {
        var menus = await _service.SaveAsync(new SaveMenusRequest(
            [new MenuInsert("USER", "Users", "P", "/bc/user", "bc", null, null)], null, [new MenuDelete(3, Read)]), "kim", None);

        menus.Single(m => m.MenuCode == "USER").Name.Should().Be("Users");
    }

    [Theory, Trait("Id", "BC-UT-201")]
    [InlineData("item")]
    [InlineData("NEW")]
    public async Task Save_AnExistingOrRepeatedCode_IsBcE201(string secondCode)
    {
        var act = () => _service.SaveAsync(new SaveMenusRequest(
            [new MenuInsert("NEW", "New", "P", "/bc/new", "bc", null, null), new MenuInsert(secondCode, "Again", "P", "/bc/x", "bc", null, null)],
            null, null), "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(MenuRules.InvalidCodeCode);
        error.Args.Should().Equal(secondCode.ToUpperInvariant());
    }

    [Fact, Trait("Id", "BC-UC-006-T02")]
    public async Task Save_WithAFailingRow_WritesAndCommitsNothing()
    {
        var act = () => _service.SaveAsync(new SaveMenusRequest(
            [new MenuInsert("NEW", "New", "P", "/bc/new", "bc", null, null)],
            [new MenuUpdate(2, "Items", "P", "/bc/item", "bc", "USER", null, Read)], null), "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(MenuRules.InvalidParentCode);
        error.Args.Should().Equal("ITEM");
        _menus.Writes.Should().Be(0);
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-006-T02")]
    public async Task Save_WithAStaleRow_Is409_AndCommitsNothing()
    {
        var act = () => _service.SaveAsync(new SaveMenusRequest(
            null, [new MenuUpdate(2, "Items", "P", "/bc/item", "bc", null, null, Read.AddSeconds(-1))], null), "kim", None);

        await act.Should().ThrowAsync<ConcurrencyException>();
        _uow.Commits.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UC-006-T02")]
    public async Task Save_WithAnUnknownValidFlag_IsCommonE002()
    {
        var act = () => _service.SaveAsync(new SaveMenusRequest(
            [new MenuInsert("NEW", "New", "P", "/bc/new", "bc", null, "X")], null, null), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(FakeCodeValidator.InvalidCode);
        _menus.Writes.Should().Be(0);
    }

    [Fact, Trait("Id", "BC-UT-205")]
    public async Task Save_MovingAGroupUnderItsOwnChild_IsBcE205()
    {
        _menus.Add(4, "MASTER", "M", 1);

        var act = () => _service.SaveAsync(new SaveMenusRequest(
            null, [new MenuUpdate(1, "Base", "M", null, "bc", "MASTER", null, Read)], null), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("BASE");
    }

    [Fact, Trait("Id", "BC-UT-205")]
    public async Task Save_ChangingAParentIntoAProgram_IsBcE205ForItsChild()
    {
        var act = () => _service.SaveAsync(new SaveMenusRequest(
            null, [new MenuUpdate(1, "Base", "P", "/bc/base", "bc", null, null, Read)], null), "kim", None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(MenuRules.InvalidParentCode);
    }

    [Fact, Trait("Id", "BC-UT-207")]
    public async Task Save_DeletingAnAssignedMenu_IsBcE207()
    {
        _menus.Assigned.Add(2);

        var act = () => _service.SaveAsync(new SaveMenusRequest(null, null, [new MenuDelete(2, Read)]), "kim", None);

        var error = (await act.Should().ThrowAsync<DomainException>()).Which;
        error.Code.Should().Be(MenuRules.InUseCode);
        error.Args.Should().Equal("ITEM");
    }

    [Fact, Trait("Id", "BC-UT-207")]
    public async Task Save_DeletingAGroupWithChildren_IsBcE207_UnlessTheChildrenGoToo()
    {
        var act = () => _service.SaveAsync(new SaveMenusRequest(null, null, [new MenuDelete(1, Read), new MenuDelete(2, Read)]), "kim", None);
        (await act.Should().ThrowAsync<DomainException>()).Which.Args.Should().Equal("BASE");

        var menus = await _service.SaveAsync(
            new SaveMenusRequest(null, null, [new MenuDelete(1, Read), new MenuDelete(2, Read), new MenuDelete(3, Read)]), "kim", None);
        menus.Should().BeEmpty();
    }

    [Fact, Trait("Id", "BC-UT-206")]
    public async Task Save_ClearsCachedPermissions_SoAMenuSetNotInUseIsDeniedNextRequest()
    {
        _grants.Grants.Add(new RoleMenuGrant("ITEM", true));
        (await _permissions.GetAccessAsync("user", "ITEM", None)).Should().Be(MenuAccess.Full);

        await _service.SaveAsync(new SaveMenusRequest(null, [new MenuUpdate(2, "Item", "P", "/bc/item", "bc", "BASE", "N", Read)], null), "kim", None);
        // Storage leaves menus not in use out of the permissions (BC-BR-206)
        _grants.Grants.Clear();

        (await _permissions.GetAccessAsync("user", "ITEM", None)).Should().Be(MenuAccess.None);
    }

    [Theory, Trait("Id", "BC-UC-006-T03")]
    [InlineData(" it ", "IT")]
    [InlineData(" ", null)]
    public async Task Search_PassesAnUpperCasePrefix(string input, string? expected)
    {
        await _service.SearchAsync(input, None);

        _menus.LastSearch.Should().Be(expected);
    }

    private sealed class FakeCodeValidator : ICodeValidator
    {
        public const string InvalidCode = "Common-E-002";

        public Task EnsureValidAsync(string majorCode, string? value, CancellationToken cancellationToken) =>
            majorCode == CodeMajors.ValidFlag && value is not ("Y" or "N")
                ? throw new DomainException(InvalidCode, majorCode, value ?? "")
                : Task.CompletedTask;
    }

    private sealed class FakeMenuRepository : IMenuRepository
    {
        private readonly Dictionary<long, (string Code, MenuFields Fields, long? ParentId, DateTimeOffset UpdatedAt)> _rows = [];
        private long _nextId = 100;

        public HashSet<long> Assigned { get; } = [];

        public int Writes { get; private set; }

        public string? LastSearch { get; private set; } = "unset";

        public void Add(long id, string code, string type, long? parentId) =>
            _rows[id] = (code, new MenuFields(code, type, type == "M" ? null : "/" + code, "bc", null, "Y"), parentId, Read);

        public Task<IReadOnlyList<MenuDto>> SearchAsync(string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            LastSearch = menuCode;
            return Task.FromResult<IReadOnlyList<MenuDto>>(_rows.Where(r => menuCode is null || r.Value.Code.StartsWith(menuCode, StringComparison.Ordinal))
                .OrderBy(r => r.Value.Code, StringComparer.Ordinal)
                .Select(r => new MenuDto(r.Key, r.Value.Code, r.Value.Fields.Name, r.Value.Fields.Type, r.Value.Fields.ProgramId, r.Value.Fields.Module,
                    r.Value.ParentId is { } p ? _rows[p].Code : null, r.Value.Fields.ValidFlag, r.Value.UpdatedAt))
                .ToList());
        }

        public Task<IReadOnlyList<MenuRecord>> LockAllAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MenuRecord>>(
                _rows.Select(r => new MenuRecord(r.Key, r.Value.Code, r.Value.Fields.Type, r.Value.ParentId, r.Value.UpdatedAt)).ToList());

        public Task<IReadOnlySet<long>> FindAssignedAsync(
            IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(menuIds.Where(Assigned.Contains).ToHashSet());

        public Task<long> InsertAsync(string menuCode, MenuFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            var id = _nextId++;
            _rows[id] = (menuCode, fields, null, Read.AddMinutes(1));
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            long id, MenuFields fields, long? parentMenuId, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            if (!_rows.TryGetValue(id, out var row) || row.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }
            _rows[id] = (row.Code, fields, parentMenuId, row.UpdatedAt.AddMinutes(1));
            return Task.FromResult(true);
        }

        public Task SetParentAsync(long id, long? parentMenuId, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes++;
            _rows[id] = _rows[id] with { ParentId = parentMenuId };
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            Writes += ids.Count;
            foreach (var id in ids)
            {
                _rows.Remove(id);
            }
            return Task.CompletedTask;
        }
    }

    // Only the permission lookup is used by the menu permission cache
    private sealed class FakeGrants : IRoleRepository
    {
        public List<RoleMenuGrant> Grants { get; } = [];

        public Task<IReadOnlyList<RoleMenuGrant>> FindPermissionsAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleMenuGrant>>(Grants.ToList());

        public Task<IReadOnlyList<RoleDto>> FindInUseAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RoleDto?> FindInUseByIdAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long?> FindIdByCodeAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RoleMenuDto>> FindMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MenuOptionDto>> FindUnassignedMenusAsync(
            long roleId, string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<long>> FindExistingMenuIdsAsync(
            IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long?> LockRoleMenuScreenAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<long>> FindFullAccessRoleIdsAsync(long menuId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task InsertMenuAsync(
            long roleId, long menuId, bool fullAccess, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> UpdateMenuAsync(
            long roleId, long menuId, bool fullAccess, DateTimeOffset expectedUpdatedAt, string updatedBy,
            IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteMenuAsync(
            long roleId, long menuId, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAllMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
