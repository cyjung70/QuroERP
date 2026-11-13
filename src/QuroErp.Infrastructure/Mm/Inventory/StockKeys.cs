namespace QuroErp.Infrastructure.Mm.Inventory;

/// <summary>
/// SQL pieces for the system Location and Lot '*' (MM-BR-713, MM-BR-363). A warehouse and an item get their '*' row when they are
/// created; rows made outside the API (fixtures, seeds, demo data) get it the first time stock or a document line needs it.
/// </summary>
internal static class StockKeys
{
    /// <summary>
    /// CTEs that create the '*' Location of @WarehouseId and the '*' Lot of @ItemId (when not null) if missing. A statement that
    /// starts with them reads the ids with <see cref="StarLocationId"/> and <see cref="StarLotId"/>: rows inserted by a CTE are
    /// not visible to the rest of the statement, so the ids come from RETURNING or from the existing row.
    /// </summary>
    public const string EnsureStarRows = """
        WITH star_location AS (
          INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
          VALUES (@WarehouseId, '*', true, @User, @User)
          ON CONFLICT (warehouse_id, location_code) DO NOTHING
          RETURNING id
        ), star_lot AS (
          INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
          SELECT @ItemId, '*', @User, @User WHERE @ItemId::bigint IS NOT NULL
          ON CONFLICT (item_id, lot_no) DO NOTHING
          RETURNING id
        )
        """;

    /// <summary>The '*' Location id of @WarehouseId, after <see cref="EnsureStarRows"/>.</summary>
    public const string StarLocationId =
        "coalesce((SELECT id FROM star_location), (SELECT id FROM bc_location WHERE warehouse_id = @WarehouseId AND location_code = '*'))";

    /// <summary>The '*' Lot id of @ItemId (null without an item), after <see cref="EnsureStarRows"/>.</summary>
    public const string StarLotId =
        "coalesce((SELECT id FROM star_lot), (SELECT id FROM mm_lot WHERE item_id = @ItemId AND lot_no = '*'))";
}
