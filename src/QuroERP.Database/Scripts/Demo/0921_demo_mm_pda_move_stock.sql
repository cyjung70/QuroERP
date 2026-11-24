-- Demo stock for the PDA move screen (TASK-MM-062, --demo only). Fictional codes only.
-- Opening stock of the lot- and Location-managed demo resin (0915) on shelf A-01 of the default warehouse (0914), in two Lots,
-- posted as goods receipts dated 2026-11-01, so a move inside the warehouse (LT, A-01 → A-02) and to the warehouse EAST (TR,
-- E-01) can be tried with a Location and a Lot. The bolt (0908) and the Pallets (0917) on Location '*' cover the moves without
-- Location management. It posts only while the resin has no movement yet, so the script can run again. Its receipts carry
-- their own ref_key, apart from the opening stock of 0908.
-- An applied script is not changed. More demo stock goes into a new script.

INSERT INTO mm_lot (item_id, lot_no, received_dt, created_by, updated_by)
SELECT i.id, v.lot_no, DATE '2026-11-01', 'system', 'system'
FROM bc_item i
CROSS JOIN (VALUES ('*'), ('R2611A'), ('R2611B')) AS v (lot_no)
WHERE i.item_cd = 'DEMO-RESIN-LOC'
ON CONFLICT (item_id, lot_no) DO NOTHING;

-- The on-hand rows and the item totals and moving average cost follow from the same receipts (MM-BR-354, MM-BR-601)
WITH opening AS (
    SELECT v.document_no, i.id AS item_id, w.id AS warehouse_id, c.id AS location_id, l.id AS lot_id, v.qty::numeric AS qty,
           v.prc::numeric AS prc, v.ref_seq
    FROM (VALUES
        ('DEMO-PM-0001', 'R2611A', 20, 3.5000, 1),
        ('DEMO-PM-0002', 'R2611B', 10, 3.5000, 2)
    ) AS v (document_no, lot_no, qty, prc, ref_seq)
    JOIN bc_item i ON i.item_cd = 'DEMO-RESIN-LOC'
    JOIN bc_warehouse w ON w.is_default
    JOIN bc_location c ON c.warehouse_id = w.id AND c.location_code = 'A-01'
    JOIN mm_lot l ON l.item_id = i.id AND l.lot_no = v.lot_no
    WHERE NOT EXISTS (SELECT 1 FROM mm_goods_movement m WHERE m.item_id = i.id)
),
movements AS (
    INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, location_id, lot_id, qty, prc, amount,
                                   ref_key, ref_seq, created_by, updated_by)
    SELECT document_no, DATE '2026-11-01', 'GR', item_id, warehouse_id, location_id, lot_id, qty, prc, qty * prc, 'DEMO-PDA-MOVE',
           ref_seq, 'system', 'system'
    FROM opening
    RETURNING id
),
onhand AS (
    INSERT INTO mm_onhand (item_id, warehouse_id, location_id, lot_id, onhand_qty, created_by, updated_by)
    SELECT item_id, warehouse_id, location_id, lot_id, sum(qty), 'system', 'system'
    FROM opening
    GROUP BY item_id, warehouse_id, location_id, lot_id
    ON CONFLICT (item_id, warehouse_id, location_id, lot_id) DO UPDATE SET onhand_qty = mm_onhand.onhand_qty + excluded.onhand_qty
    RETURNING id
)
UPDATE bc_item i
SET onhand_qty = coalesce(i.onhand_qty, 0) + o.qty,
    stk_val = coalesce(i.stk_val, 0) + o.amount,
    mvg_cost = (coalesce(i.stk_val, 0) + o.amount) / (coalesce(i.onhand_qty, 0) + o.qty),
    updated_by = 'system', updated_at = now()
FROM (SELECT item_id, sum(qty) AS qty, sum(qty * prc) AS amount FROM opening GROUP BY item_id) o
WHERE i.id = o.item_id;
