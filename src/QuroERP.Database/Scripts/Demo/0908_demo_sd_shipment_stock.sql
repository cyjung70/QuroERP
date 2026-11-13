-- Demo stock for the shipment screen (--demo only). Fictional codes and names only.
-- Opening stock in the company default warehouse, posted as goods receipts dated 2026-11-01 at the demo purchase prices, for
-- the two demo inventory items (0903) and a new lot-managed demo item with two lots, so a shipment can issue stock and pick a
-- lot (MM-BR-355, MM-BR-362). No demo sales orders or shipments: they are made on the screens.
-- Stock is kept per item + warehouse + Location + Lot (MM-BR-714, TASK-CORE-067): the receipts use Location '*' and, for items
-- without lot management, Lot '*'. The demo items and warehouses made by SQL here and in 0902~0904 get their '*' rows first.
-- Changed with TASK-CORE-067 (approved exception): demo scripts never run on the dev server, and databases that applied the
-- earlier version are converted by schema script 0418.

INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, usd_prc, so_usd_prc, lot_managed, lot_managed_from, created_by, updated_by)
SELECT 'DEMO-CABLE-LOT', 'Demo Cable Reel (lot)', 'INVENTORY', 'EA', 'Y', 12.0000, 20.0000, true, DATE '2026-11-01', 'system', 'system'
WHERE NOT EXISTS (SELECT 1 FROM bc_item i WHERE i.item_cd = 'DEMO-CABLE-LOT');

-- The system Location of every warehouse and the system Lot of every item (MM-BR-712, MM-BR-363)
INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
SELECT w.id, '*', true, 'system', 'system' FROM bc_warehouse w
ON CONFLICT (warehouse_id, location_code) DO NOTHING;
INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
SELECT i.id, '*', 'system', 'system' FROM bc_item i
ON CONFLICT (item_id, lot_no) DO NOTHING;

INSERT INTO mm_lot (item_id, lot_no, received_dt, created_by, updated_by)
SELECT i.id, v.lot_no, DATE '2026-11-01', 'system', 'system'
FROM bc_item i
CROSS JOIN (VALUES ('L2611A'), ('L2611B')) AS v (lot_no)
WHERE i.item_cd = 'DEMO-CABLE-LOT'
ON CONFLICT (item_id, lot_no) DO NOTHING;

-- One receipt per item and lot, only for items with no movement yet; the on-hand rows and the item totals and moving average
-- cost follow from the same receipts (MM-BR-354, MM-BR-601)
WITH opening AS (
    SELECT v.document_no, i.id AS item_id, w.id AS warehouse_id, c.id AS location_id, l.id AS lot_id, v.qty::numeric AS qty,
           v.prc::numeric AS prc, v.ref_seq
    FROM (VALUES
        ('DEMO-OB-0001', 'DEMO-BOLT-10', '*', 1000, 0.4500, 1),
        ('DEMO-OB-0002', 'DEMO-PANEL-01', '*', 40, 128.0000, 2),
        ('DEMO-OB-0003', 'DEMO-CABLE-LOT', 'L2611A', 30, 12.0000, 3),
        ('DEMO-OB-0004', 'DEMO-CABLE-LOT', 'L2611B', 20, 12.0000, 4)
    ) AS v (document_no, item_cd, lot_no, qty, prc, ref_seq)
    JOIN bc_item i ON i.item_cd = v.item_cd
    JOIN bc_warehouse w ON w.is_default
    JOIN bc_location c ON c.warehouse_id = w.id AND c.location_code = '*'
    JOIN mm_lot l ON l.item_id = i.id AND l.lot_no = v.lot_no
    WHERE NOT EXISTS (SELECT 1 FROM mm_goods_movement m WHERE m.item_id = i.id)
),
movements AS (
    INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, location_id, lot_id, qty, prc, amount,
                                   ref_key, ref_seq, created_by, updated_by)
    SELECT document_no, DATE '2026-11-01', 'GR', item_id, warehouse_id, location_id, lot_id, qty, prc, qty * prc, 'DEMO-OPENING',
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