namespace QuroErp.Infrastructure.Mm.Inventory;

/// <summary>SQL shared by the Lot search, the stock suggestion and the Kit component allocation.</summary>
internal static class StockBalanceSql
{
    // MM-BR-362, MM-BR-716: balance of each (Location, Lot) at the end of @AsOf = current balance minus the movements of that key
    // dated after it. Locations not in use cannot be picked for new lines (MM-BR-717)
    public const string SelectBalances = $"""
        SELECT location_id, location_code, lot_id, lot_no, received_dt, qty
        FROM (
          SELECT o.location_id, c.location_code, o.lot_id, l.lot_no, l.received_dt,
                 {BalanceAsOf} AS qty
          FROM mm_onhand o
          JOIN bc_location c ON c.id = o.location_id
          JOIN mm_lot l ON l.id = o.lot_id
          WHERE o.item_id = @ItemId AND o.warehouse_id = @WarehouseId AND c.use_yn
        ) b
        WHERE qty > 0
        """;

    // MM-BR-716, MM-BR-916: what a line that is not a Pallet line may take: the balance minus the ACTIVE Pallets of the key
    public const string SelectAvailableBalances = $"""
        SELECT location_id, location_code, lot_id, lot_no, received_dt, qty
        FROM (
          SELECT o.location_id, c.location_code, o.lot_id, l.lot_no, l.received_dt,
                 {BalanceAsOf} - {ActivePalletQty} AS qty
          FROM mm_onhand o
          JOIN bc_location c ON c.id = o.location_id
          JOIN mm_lot l ON l.id = o.lot_id
          WHERE o.item_id = @ItemId AND o.warehouse_id = @WarehouseId AND c.use_yn
        ) b
        WHERE qty > 0
        """;

    // MM-BR-916: the quantity the ACTIVE Pallets of one key hold
    public const string SelectKeyPalletQty = """
        SELECT coalesce(sum(qty), 0) FROM mm_pallet
        WHERE item_id = @ItemId AND warehouse_id = @WarehouseId AND location_id = @LocationId AND lot_id = @LotId AND status = 'ACTIVE'
        """;

    private const string BalanceAsOf = """
        o.onhand_qty - coalesce((SELECT sum(m.qty) FROM mm_goods_movement m
                                 WHERE m.item_id = o.item_id AND m.warehouse_id = o.warehouse_id
                                   AND m.location_id = o.location_id AND m.lot_id = o.lot_id
                                   AND m.document_dt > @AsOf), 0)
        """;

    private const string ActivePalletQty = """
        coalesce((SELECT sum(p.qty) FROM mm_pallet p
                  WHERE p.item_id = o.item_id AND p.warehouse_id = o.warehouse_id
                    AND p.location_id = o.location_id AND p.lot_id = o.lot_id AND p.status = 'ACTIVE'), 0)
        """;
}
