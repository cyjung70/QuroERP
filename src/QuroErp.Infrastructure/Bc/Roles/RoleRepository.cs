using System.Data;
using Dapper;
using QuroErp.Application.Bc.Roles;
using QuroErp.Contracts.Bc.Roles;
using QuroErp.Domain.Bc.Roles;

namespace QuroErp.Infrastructure.Bc.Roles;

internal sealed class RoleRepository : IRoleRepository
{
    private const string SelectInUse = """
        SELECT id, role_code FROM sys_role WHERE use_yn ORDER BY sort_order NULLS LAST, role_code
        """;

    private const string SelectInUseById = "SELECT id, role_code FROM sys_role WHERE id = @RoleId AND use_yn";

    private const string SelectIdByCode = "SELECT id FROM sys_role WHERE role_code = @RoleCode";

    private const string SelectMenus = """
        SELECT m.id AS menu_id, m.menu_code, m.name, m.type, m.module, rm.full_access, rm.updated_at
        FROM sys_role_menu rm
        JOIN sys_menu m ON m.id = rm.menu_id
        WHERE rm.role_id = @RoleId
        ORDER BY m.menu_code
        """;

    private const string SelectUnassigned = """
        SELECT m.id AS menu_id, m.menu_code, m.name, m.type, m.module
        FROM sys_menu m
        WHERE NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = @RoleId AND rm.menu_id = m.id)
          AND (@MenuCodePattern IS NULL OR m.menu_code LIKE @MenuCodePattern ESCAPE '\')
        ORDER BY m.menu_code
        """;

    private const string SelectExistingMenuIds = "SELECT id FROM sys_menu WHERE id = ANY(@MenuIds)";

    private const string LockRoleMenuScreen = """
        SELECT id FROM sys_menu WHERE program_id = @ProgramId ORDER BY id LIMIT 1 FOR UPDATE
        """;

    private const string SelectFullAccessRoleIds = """
        SELECT rm.role_id
        FROM sys_role_menu rm
        JOIN sys_role r ON r.id = rm.role_id
        WHERE rm.menu_id = @MenuId AND rm.full_access = @Yes AND r.use_yn
        """;

    private const string InsertMenu = """
        INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
        VALUES (@RoleId, @MenuId, @FullAccess, @By, @By)
        """;

    private const string UpdateMenu = """
        UPDATE sys_role_menu SET full_access = @FullAccess, updated_at = now(), updated_by = @By
        WHERE role_id = @RoleId AND menu_id = @MenuId AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteMenu = """
        DELETE FROM sys_role_menu WHERE role_id = @RoleId AND menu_id = @MenuId AND updated_at = @ExpectedUpdatedAt
        """;

    private const string DeleteAllMenus = "DELETE FROM sys_role_menu WHERE role_id = @RoleId";

    // BC-BR-206: menus not in use give no permission; a role not in use gives none either
    private const string SelectPermissions = """
        SELECT m.menu_code, rm.full_access
        FROM sys_role r
        JOIN sys_role_menu rm ON rm.role_id = r.id
        JOIN sys_menu m ON m.id = rm.menu_id
        WHERE r.role_code = @RoleCode AND r.use_yn AND m.valid_flag = @Yes
        """;

    public async Task<IReadOnlyList<RoleDto>> FindInUseAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<RoleDto>(
            new CommandDefinition(SelectInUse, transaction: transaction, cancellationToken: cancellationToken))).ToList();

    public Task<RoleDto?> FindInUseByIdAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.QuerySingleOrDefaultAsync<RoleDto?>(
            new CommandDefinition(SelectInUseById, new { RoleId = roleId }, transaction, cancellationToken: cancellationToken));

    public Task<long?> FindIdByCodeAsync(string roleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long?>(
            new CommandDefinition(SelectIdByCode, new { RoleCode = roleCode }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<RoleMenuDto>> FindMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<MenuRow>(
            new CommandDefinition(SelectMenus, new { RoleId = roleId }, transaction, cancellationToken: cancellationToken)))
        .Select(r => r.ToDto()).ToList();

    public async Task<IReadOnlyList<MenuOptionDto>> FindUnassignedMenusAsync(
        long roleId, string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<MenuOptionDto>(new CommandDefinition(
            SelectUnassigned,
            new { RoleId = roleId, MenuCodePattern = menuCode is null ? null : EscapeLike(menuCode) + "%" },
            transaction,
            cancellationToken: cancellationToken))).ToList();

    public async Task<IReadOnlySet<long>> FindExistingMenuIdsAsync(
        IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        menuIds.Count == 0
            ? new HashSet<long>()
            : (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
                SelectExistingMenuIds, new { MenuIds = menuIds.ToArray() }, transaction, cancellationToken: cancellationToken))).ToHashSet();

    public Task<long?> LockRoleMenuScreenAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long?>(new CommandDefinition(
            LockRoleMenuScreen, new { ProgramId = RoleMenuRules.RoleMenuProgramId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlySet<long>> FindFullAccessRoleIdsAsync(long menuId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
            SelectFullAccessRoleIds, new { MenuId = menuId, Yes = RoleMenuRules.FullAccessYes }, transaction,
            cancellationToken: cancellationToken))).ToHashSet();

    public Task InsertMenuAsync(
        long roleId, long menuId, bool fullAccess, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            InsertMenu, new { RoleId = roleId, MenuId = menuId, FullAccess = RoleMenuRules.ToFlag(fullAccess), By = createdBy }, transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateMenuAsync(
        long roleId, long menuId, bool fullAccess, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            UpdateMenu,
            new
            {
                RoleId = roleId, MenuId = menuId, FullAccess = RoleMenuRules.ToFlag(fullAccess), By = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public async Task<bool> DeleteMenuAsync(
        long roleId, long menuId, DateTimeOffset expectedUpdatedAt, IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            DeleteMenu, new { RoleId = roleId, MenuId = menuId, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime }, transaction,
            cancellationToken: cancellationToken)) == 1;

    public Task DeleteAllMenusAsync(long roleId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(
            new CommandDefinition(DeleteAllMenus, new { RoleId = roleId }, transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<RoleMenuGrant>> FindPermissionsAsync(
        string roleCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(string MenuCode, string? FullAccess)>(new CommandDefinition(
            SelectPermissions, new { RoleCode = roleCode, Yes = AppMenuValidFlag }, transaction, cancellationToken: cancellationToken)))
        .Select(r => new RoleMenuGrant(r.MenuCode, RoleMenuRules.IsFullAccess(r.FullAccess))).ToList();

    private const string AppMenuValidFlag = "Y";

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record MenuRow(long MenuId, string MenuCode, string? Name, string? Type, string? Module, string? FullAccess, DateTime UpdatedAt)
    {
        public RoleMenuDto ToDto() => new(
            MenuId, MenuCode, Name, Type, Module, RoleMenuRules.IsFullAccess(FullAccess),
            new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
    }
}
