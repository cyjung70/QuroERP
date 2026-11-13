namespace QuroErp.Infrastructure.Tests;

/// <summary>
/// SQL for fixtures that write stock rows directly: the system Location and Lot '*' of warehouses and items the fixture made with
/// SQL (MM-BR-713, MM-BR-363). The API makes them when it creates a warehouse or an item.
/// </summary>
internal static class StarKeys
{
    /// <summary>Creates the '*' rows of the warehouses and items the two SQL expressions list, when missing.</summary>
    public static string Ensure(string warehouseIds, string itemIds) => $"""
        INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
        SELECT id, '*', true, 't', 't' FROM bc_warehouse WHERE id IN ({warehouseIds})
        ON CONFLICT (warehouse_id, location_code) DO NOTHING;
        INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
        SELECT id, '*', 't', 't' FROM bc_item WHERE id IN ({itemIds})
        ON CONFLICT (item_id, lot_no) DO NOTHING;
        """;

    /// <summary>The '*' Location id of the warehouse the SQL expression gives.</summary>
    public static string Location(string warehouseId) => $"(SELECT id FROM bc_location WHERE warehouse_id = {warehouseId} AND location_code = '*')";

    /// <summary>The '*' Lot id of the item the SQL expression gives.</summary>
    public static string Lot(string itemId) => $"(SELECT id FROM mm_lot WHERE item_id = {itemId} AND lot_no = '*')";
}
