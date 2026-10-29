using System.Data;
using Dapper;
using QuroErp.Application.Bc.UserProfiles;
using QuroErp.Domain.Bc.UserProfiles;

namespace QuroErp.Infrastructure.Bc.UserProfiles;

internal sealed class UserProfileRepository : IUserProfileRepository
{
    // The user's warehouse, or the company default warehouse when the user has none
    private const string SelectById = """
        SELECT u.id, u.userid AS user_id, u.username AS user_name, u.email, u.phone, u.lang_code, u.default_warehouse_id,
               w.warehouse_code AS default_warehouse_code, w.warehouse_name AS default_warehouse_name, u.updated_at
        FROM sys_app_user u
        LEFT JOIN bc_warehouse w ON w.id = u.default_warehouse_id OR (u.default_warehouse_id IS NULL AND w.is_default)
        WHERE u.id = @Id
        """;

    // FOR UPDATE OF u: lock only the user row, not the warehouse row
    private const string SelectByIdForUpdate = SelectById + " FOR UPDATE OF u";

    private const string SelectLanguageInUse = "SELECT EXISTS (SELECT 1 FROM sys_language WHERE lang_code = @LangCode AND use_yn)";

    private const string SelectWarehouseInUse = "SELECT EXISTS (SELECT 1 FROM bc_warehouse WHERE id = @Id AND use_yn)";

    private const string Update = """
        UPDATE sys_app_user
        SET username = @UserName, email = @Email, phone = @Phone, lang_code = @LangCode, default_warehouse_id = @DefaultWarehouseId,
            updated_at = now(), updated_by = @UpdatedBy
        WHERE id = @Id AND updated_at = @ExpectedUpdatedAt
        """;

    public async Task<UserProfileRow?> FindAsync(long appUserId, bool forUpdate, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            forUpdate ? SelectByIdForUpdate : SelectById, new { Id = appUserId }, transaction, cancellationToken: cancellationToken));
        return row?.ToUserProfileRow();
    }

    public Task<bool> IsLanguageInUseAsync(string langCode, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectLanguageInUse, new { LangCode = langCode }, transaction, cancellationToken: cancellationToken));

    public Task<bool> IsWarehouseInUseAsync(long warehouseId, IDbTransaction transaction, CancellationToken cancellationToken) =>
        transaction.Connection!.ExecuteScalarAsync<bool>(
            new CommandDefinition(SelectWarehouseInUse, new { Id = warehouseId }, transaction, cancellationToken: cancellationToken));

    public async Task<bool> UpdateAsync(
        long appUserId, UserProfileFields fields, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken) =>
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            Update,
            new
            {
                Id = appUserId,
                fields.UserName,
                fields.Email,
                fields.Phone,
                fields.LangCode,
                fields.DefaultWarehouseId,
                UpdatedBy = updatedBy,
                ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime,
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(
        long Id,
        string UserId,
        string? UserName,
        string? Email,
        string? Phone,
        string? LangCode,
        long? DefaultWarehouseId,
        string? DefaultWarehouseCode,
        string? DefaultWarehouseName,
        DateTime UpdatedAt)
    {
        public UserProfileRow ToUserProfileRow() => new(
            Id, UserId, UserName, Email, Phone, LangCode, DefaultWarehouseId, DefaultWarehouseCode, DefaultWarehouseName,
            new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
    }
}
