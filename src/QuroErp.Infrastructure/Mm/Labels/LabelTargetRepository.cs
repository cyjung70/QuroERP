using System.Data;
using Dapper;
using QuroErp.Application.Mm.Labels;
using QuroErp.Contracts.Mm.Labels;
using QuroErp.Domain.Mm.Labels;

namespace QuroErp.Infrastructure.Mm.Labels;

internal sealed class LabelTargetRepository : ILabelTargetRepository
{
    // Every query returns the LabelTargetDto columns in order
    private const string Pallets = """
        SELECT p.id, 'PALLET' AS label_type, p.pallet_no AS code, i.item_name AS name, i.item_cd, lot.lot_no, p.qty, lot.expiry_dt,
               w.warehouse_code, loc.location_code
        FROM mm_pallet p
        JOIN bc_item i ON i.id = p.item_id
        JOIN mm_lot lot ON lot.id = p.lot_id
        JOIN bc_warehouse w ON w.id = p.warehouse_id
        JOIN bc_location loc ON loc.id = p.location_id
        LEFT JOIN mm_goods_receipt_line l ON l.id = p.goods_receipt_line_id
        WHERE p.status = 'ACTIVE'
          AND (@Ids::bigint[] IS NULL OR p.id = ANY(@Ids))
          AND (@GoodsReceiptId::bigint IS NULL OR l.goods_receipt_id = @GoodsReceiptId)
          AND (@ItemId::bigint IS NULL OR p.item_id = @ItemId)
          AND (@WarehouseId::bigint IS NULL OR p.warehouse_id = @WarehouseId)
        ORDER BY p.pallet_no
        LIMIT @Limit
        """;

    // Items in use; with a receipt, the items on its lines
    private const string Items = """
        SELECT i.id, 'ITEM' AS label_type, i.item_cd AS code, i.item_name AS name, i.item_cd, NULL::varchar AS lot_no,
               NULL::numeric AS qty, NULL::date AS expiry_dt, NULL::varchar AS warehouse_code, NULL::varchar AS location_code
        FROM bc_item i
        WHERE coalesce(i.valid_flag, 'Y') <> 'N'
          AND (@Ids::bigint[] IS NULL OR i.id = ANY(@Ids))
          AND (@ItemId::bigint IS NULL OR i.id = @ItemId)
          AND (@GoodsReceiptId::bigint IS NULL
               OR EXISTS (SELECT 1 FROM mm_goods_receipt_line l WHERE l.goods_receipt_id = @GoodsReceiptId AND l.item_id = i.id))
        ORDER BY i.item_cd
        LIMIT @Limit
        """;

    // With a receipt, the Lots it received
    private const string Lots = """
        SELECT lot.id, 'LOT' AS label_type, lot.lot_no AS code, i.item_name AS name, i.item_cd, lot.lot_no, NULL::numeric AS qty,
               lot.expiry_dt, NULL::varchar AS warehouse_code, NULL::varchar AS location_code
        FROM mm_lot lot
        JOIN bc_item i ON i.id = lot.item_id
        WHERE (@Ids::bigint[] IS NULL OR lot.id = ANY(@Ids))
          AND (@ItemId::bigint IS NULL OR lot.item_id = @ItemId)
          AND (@GoodsReceiptId::bigint IS NULL
               OR EXISTS (SELECT 1 FROM mm_goods_receipt_line l WHERE l.goods_receipt_id = @GoodsReceiptId AND l.lot_id = lot.id))
        ORDER BY i.item_cd, lot.lot_no
        LIMIT @Limit
        """;

    // Locations in use; the system Location '*' is not a shelf and gets no label
    private const string Locations = """
        SELECT loc.id, 'LOCATION' AS label_type, loc.location_code AS code, loc.location_name AS name, NULL::varchar AS item_cd,
               NULL::varchar AS lot_no, NULL::numeric AS qty, NULL::date AS expiry_dt, w.warehouse_code, loc.location_code
        FROM bc_location loc
        JOIN bc_warehouse w ON w.id = loc.warehouse_id
        WHERE NOT loc.is_system AND loc.use_yn
          AND (@Ids::bigint[] IS NULL OR loc.id = ANY(@Ids))
          AND (@WarehouseId::bigint IS NULL OR loc.warehouse_id = @WarehouseId)
        ORDER BY w.warehouse_code, loc.location_code
        LIMIT @Limit
        """;

    public async Task<IReadOnlyList<LabelTargetDto>> FindAsync(
        string labelType, LabelTargetFilter filter, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var sql = labelType switch
        {
            LabelType.Pallet => Pallets,
            LabelType.Item => Items,
            LabelType.Lot => Lots,
            LabelType.Location => Locations,
            _ => throw new ArgumentException($"Unknown label type {labelType}.", nameof(labelType)),
        };
        var rows = await transaction.Connection!.QueryAsync<LabelTargetDto>(new CommandDefinition(
            sql,
            new { Ids = filter.Ids?.ToArray(), filter.GoodsReceiptId, filter.ItemId, filter.WarehouseId, filter.Limit },
            transaction, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
