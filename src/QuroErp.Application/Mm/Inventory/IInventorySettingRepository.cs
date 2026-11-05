using System.Data;

namespace QuroErp.Application.Mm.Inventory;

/// <summary>The single mm_inventory_setting row. The modes are the stored string codes.</summary>
public sealed record InventorySettingRow(
    string NegativeStockMode, string OverReceiptMode, bool AllowCostAdjustmentEdit, DateTimeOffset UpdatedAt, string UpdatedBy);

public interface IInventorySettingRepository
{
    /// <returns>null only when the row is missing (the migration always creates it).</returns>
    Task<InventorySettingRow?> GetAsync(IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Updates the row only when updated_at still equals expectedUpdatedAt.</summary>
    /// <returns>The new row, or null when updated_at changed (concurrent update) or the row is missing.</returns>
    Task<InventorySettingRow?> UpdateAsync(
        string negativeStockMode, string overReceiptMode, bool allowCostAdjustmentEdit, DateTimeOffset expectedUpdatedAt, string updatedBy,
        IDbTransaction transaction, CancellationToken cancellationToken);
}
