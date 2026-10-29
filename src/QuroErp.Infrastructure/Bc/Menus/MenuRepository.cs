using System.Data;
using Dapper;
using QuroErp.Application.Bc.Menus;
using QuroErp.Contracts.Bc.Menus;
using QuroErp.Domain.Bc.Menus;

namespace QuroErp.Infrastructure.Bc.Menus;

internal sealed class MenuRepository : IMenuRepository
{
    private const string Search = """
        SELECT m.id, m.menu_code, m.name, m.type, m.program_id, m.module, p.menu_code AS parent_menu_code, m.valid_flag, m.updated_at
        FROM sys_menu m
        LEFT JOIN sys_menu p ON p.id = m.parent_menu_id
        WHERE @MenuCodePattern IS NULL OR m.menu_code LIKE @MenuCodePattern ESCAPE '\'
        ORDER BY m.menu_code
        """;

    private const string LockAll = "SELECT id, menu_code, type, parent_menu_id, updated_at FROM sys_menu ORDER BY id FOR UPDATE";

    private const string SelectAssigned = "SELECT DISTINCT menu_id FROM sys_role_menu WHERE menu_id = ANY(@Ids)";

    private const string Insert = """
        INSERT INTO sys_menu (menu_code, name, type, program_id, module, valid_flag, created_by, updated_by)
        VALUES (@MenuCode, @Name, @Type, @ProgramId, @Module, @ValidFlag, @By, @By)
        RETURNING id
        """;

    private const string Update = """
        UPDATE sys_menu
        SET name = @Name, type = @Type, program_id = @ProgramId, module = @Module, parent_menu_id = @ParentMenuId, valid_flag = @ValidFlag,
            updated_at = now(), updated_by = @By
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    private const string SetParent = "UPDATE sys_menu SET parent_menu_id = @ParentMenuId WHERE id = @Id";

    private const string Delete = "DELETE FROM sys_menu WHERE id = ANY(@Ids)";

    public async Task<IReadOnlyList<MenuDto>> SearchAsync(string? menuCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<MenuRow>(new CommandDefinition(
            Search, new { MenuCodePattern = menuCode is null ? null : EscapeLike(menuCode) + "%" }, transaction, cancellationToken: cancellationToken)))
        .Select(r => r.ToDto()).ToList();

    public async Task<IReadOnlyList<MenuRecord>> LockAllAsync(IDbTransaction transaction, CancellationToken cancellationToken) =>
        (await transaction.Connection!.QueryAsync<(long Id, string MenuCode, string? Type, long? ParentMenuId, DateTime UpdatedAt)>(
            new CommandDefinition(LockAll, transaction: transaction, cancellationToken: cancellationToken)))
        .Select(r => new MenuRecord(r.Id, r.MenuCode, r.Type, r.ParentMenuId, Utc(r.UpdatedAt))).ToList();

    public async Task<IReadOnlySet<long>> FindAssignedAsync(
        IReadOnlyCollection<long> menuIds, IDbTransaction transaction, CancellationToken cancellationToken) =>
        menuIds.Count == 0
            ? new HashSet<long>()
            : (await transaction.Connection!.QueryAsync<long>(new CommandDefinition(
                SelectAssigned, new { Ids = menuIds.ToArray() }, transaction, cancellationToken: cancellationToken))).ToHashSet();

    public Task<long> InsertAsync(string menuCode, MenuFields fields, string createdBy, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            Insert,
            new { MenuCode = menuCode, fields.Name, fields.Type, fields.ProgramId, fields.Module, fields.ValidFlag, By = createdBy },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long id, MenuFields fields, long? parentMenuId, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Id = id, fields.Name, fields.Type, fields.ProgramId, fields.Module, ParentMenuId = parentMenuId, fields.ValidFlag, By = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    public Task SetParentAsync(long id, long? parentMenuId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteAsync(new CommandDefinition(
            SetParent, new { Id = id, ParentMenuId = parentMenuId }, transaction, cancellationToken: cancellationToken));

    // One statement, so a deleted parent and its deleted children pass the parent foreign key together
    public Task DeleteAsync(IReadOnlyCollection<long> ids, IDbTransaction transaction, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? Task.CompletedTask
            : transaction.Connection!.ExecuteAsync(new CommandDefinition(Delete, new { Ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record MenuRow(
        long Id, string MenuCode, string? Name, string? Type, string? ProgramId, string? Module, string? ParentMenuCode, string? ValidFlag,
        DateTime UpdatedAt)
    {
        public MenuDto ToDto() => new(Id, MenuCode, Name, Type, ProgramId, Module, ParentMenuCode, ValidFlag, Utc(UpdatedAt));
    }
}
