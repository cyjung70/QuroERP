namespace QuroErp.Infrastructure.Mm.Inventory;

/// <summary>SQL shared by the Lot search, the stock suggestion and the Kit component allocation.</summary>
internal static class StockBalanceSql
{
    // MM-BR-362, MM-BR-716: balance of each (Location, Lot) at the end of @AsOf = current balance minus the movements of that key
    // dated after it. Locations not in use cannot be picked for new lines (MM-BR-717)
    public const string SelectBalances = """
        SELECT location_id, location_code, lot_id, lot_no, received_dt, qty
        FROM (
          SELECT o.location_id, c.location_code, o.lot_id, l.lot_no, l.received_dt,
                 o.onhand_qty - coalesce((SELECT sum(m.qty) FROM mm_goods_movement m
                                          WHERE m.item_id = o.item_id AND m.warehouse_id = o.warehouse_id
                                            AND m.location_id = o.location_id AND m.lot_id = o.lot_id
                                            AND m.document_dt > @AsOf), 0) AS qty
          FROM mm_onhand o
          JOIN bc_location c ON c.id = o.location_id
          JOIN mm_lot l ON l.id = o.lot_id
          WHERE o.item_id = @ItemId AND o.warehouse_id = @WarehouseId AND c.use_yn
        ) b
        WHERE qty > 0
        """;
}
