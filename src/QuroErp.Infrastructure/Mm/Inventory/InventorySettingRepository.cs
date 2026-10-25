using System.Data;
using Dapper;
using QuroErp.Application.Mm.Inventory;

namespace QuroErp.Infrastructure.Mm.Inventory;

internal sealed class InventorySettingRepository : IInventorySettingRepository
{
    // One row only (uq_mm_inventory_setting_single_row)
    private const string Select = """
        SELECT negative_stock_mode, updated_at, updated_by
        FROM mm_inventory_setting
        """;

    private const string Update = """
        UPDATE mm_inventory_setting
        SET negative_stock_mode = @NegativeStockMode, updated_at = now(), updated_by = @UpdatedBy
        WHERE updated_at = @ExpectedUpdatedAt
        RETURNING negative_stock_mode, updated_at, updated_by
        """;

    public async Task<InventorySettingRow?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Select, transaction: transaction, cancellationToken: cancellationToken));
        return row?.ToSetting();
    }

    public async Task<InventorySettingRow?> UpdateAsync(
        string negativeStockMode, DateTimeOffset expectedUpdatedAt, string updatedBy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Update,
            // Npgsql only accepts UTC values for timestamptz parameters
            new { NegativeStockMode = negativeStockMode, ExpectedUpdatedAt = expectedUpdatedAt.UtcDateTime, UpdatedBy = updatedBy },
            transaction,
            cancellationToken: cancellationToken));
        return row?.ToSetting();
    }

    // Npgsql reads timestamptz as a UTC DateTime; Dapper does not convert it to DateTimeOffset for constructor binding
    private sealed record Row(string NegativeStockMode, DateTime UpdatedAt, string UpdatedBy)
    {
        public InventorySettingRow ToSetting() =>
            new(NegativeStockMode, new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)), UpdatedBy);
    }
}
