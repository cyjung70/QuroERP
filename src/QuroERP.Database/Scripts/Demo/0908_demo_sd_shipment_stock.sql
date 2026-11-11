-- Demo stock for the shipment screen (--demo only). Fictional codes and names only.
-- Opening stock in the company default warehouse, posted as goods receipts dated 2026-11-01 at the demo purchase prices, for
-- the two demo inventory items (0903) and a new lot-managed demo item with two lots, so a shipment can issue stock and pick a
-- lot (MM-BR-355, MM-BR-362). No demo sales orders or shipments: they are made on the screens.
-- An applied script is not changed. More demo data goes into a new script.

INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, usd_prc, so_usd_prc, lot_managed, lot_managed_from, created_by, updated_by)
SELECT 'DEMO-CABLE-LOT', 'Demo Cable Reel (lot)', 'INVENTORY', 'EA', 'Y', 12.0000, 20.0000, true, DATE '2026-11-01', 'system', 'system'
WHERE NOT EXISTS (SELECT 1 FROM bc_item i WHERE i.item_cd = 'DEMO-CABLE-LOT');

INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
SELECT i.id, v.lot_no, 'system', 'system'
FROM bc_item i
CROSS JOIN (VALUES ('L2611A'), ('L2611B')) AS v (lot_no)
WHERE i.item_cd = 'DEMO-CABLE-LOT'
ON CONFLICT (item_id, lot_no) DO NOTHING;

-- One receipt per item and lot, only for items with no movement yet; the on-hand rows, lot balances and the item totals and
-- moving average cost follow from the same receipts (MM-BR-354, MM-BR-601)
WITH opening AS (
    SELECT v.document_no, i.id AS item_id, w.id AS warehouse_id, l.id AS lot_id, v.qty::numeric AS qty, v.prc::numeric AS prc, v.ref_seq
    FROM (VALUES
        ('DEMO-OB-0001', 'DEMO-BOLT-10', NULL, 1000, 0.4500, 1),
        ('DEMO-OB-0002', 'DEMO-PANEL-01', NULL, 40, 128.0000, 2),
        ('DEMO-OB-0003', 'DEMO-CABLE-LOT', 'L2611A', 30, 12.0000, 3),
        ('DEMO-OB-0004', 'DEMO-CABLE-LOT', 'L2611B', 20, 12.0000, 4)
    ) AS v (document_no, item_cd, lot_no, qty, prc, ref_seq)
    JOIN bc_item i ON i.item_cd = v.item_cd
    JOIN bc_warehouse w ON w.is_default
    LEFT JOIN mm_lot l ON l.item_id = i.id AND l.lot_no = v.lot_no
    WHERE NOT EXISTS (SELECT 1 FROM mm_goods_movement m WHERE m.item_id = i.id)
),
movements AS (
    INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, qty, prc, amount, ref_key, ref_seq, lot_id,
                                   created_by, updated_by)
    SELECT document_no, DATE '2026-11-01', 'GR', item_id, warehouse_id, qty, prc, qty * prc, 'DEMO-OPENING', ref_seq, lot_id, 'system', 'system'
    FROM opening
    RETURNING id
),
lots AS (
    INSERT INTO mm_lot_onhand (item_id, warehouse_id, lot_id, onhand_qty, created_by, updated_by)
    SELECT item_id, warehouse_id, lot_id, qty, 'system', 'system'
    FROM opening
    WHERE lot_id IS NOT NULL
    ON CONFLICT (item_id, warehouse_id, lot_id) DO UPDATE SET onhand_qty = mm_lot_onhand.onhand_qty + excluded.onhand_qty
    RETURNING id
),
onhand AS (
    INSERT INTO mm_onhand (item_id, warehouse_id, onhand_qty, created_by, updated_by)
    SELECT item_id, warehouse_id, sum(qty), 'system', 'system'
    FROM opening
    GROUP BY item_id, warehouse_id
    ON CONFLICT (item_id, warehouse_id) DO UPDATE SET onhand_qty = mm_onhand.onhand_qty + excluded.onhand_qty
    RETURNING id
)
UPDATE bc_item i
SET onhand_qty = coalesce(i.onhand_qty, 0) + o.qty,
    stk_val = coalesce(i.stk_val, 0) + o.amount,
    mvg_cost = (coalesce(i.stk_val, 0) + o.amount) / (coalesce(i.onhand_qty, 0) + o.qty),
    updated_by = 'system', updated_at = now()
FROM (SELECT item_id, sum(qty) AS qty, sum(qty * prc) AS amount FROM opening GROUP BY item_id) o
WHERE i.id = o.item_id;
