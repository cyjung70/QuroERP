-- Demo Pallets for the Pallet screen (TASK-MM-054, --demo only). Fictional codes only.
-- Three Pallets of the lot-managed demo cable reel (0908) on Location '*' of the default warehouse: two on lot L2611A, to merge,
-- and one on lot L2611B, whose merge into the others is refused (MM-BR-915). Each has a CREATE event.
-- The numbers sit far above what the PL counter of the month issues, so a real Pallet never takes one of them. A Pallet is
-- added only while the ACTIVE Pallets of its key stay within the key's balance (MM-BR-916), so the script can run again.
-- An applied script is not changed. More demo Pallets go into a new script.

WITH wanted AS (
    SELECT v.pallet_no, i.id AS item_id, l.id AS lot_id, w.id AS warehouse_id, c.id AS location_id, v.qty::numeric AS qty
    FROM (VALUES
        ('PL2611990001', 'L2611A', 4),
        ('PL2611990002', 'L2611A', 4),
        ('PL2611990003', 'L2611B', 3)
    ) AS v (pallet_no, lot_no, qty)
    JOIN bc_item i ON i.item_cd = 'DEMO-CABLE-LOT'
    JOIN mm_lot l ON l.item_id = i.id AND l.lot_no = v.lot_no
    JOIN bc_warehouse w ON w.is_default
    JOIN bc_location c ON c.warehouse_id = w.id AND c.location_code = '*'
    WHERE NOT EXISTS (SELECT 1 FROM mm_pallet p WHERE p.pallet_no = v.pallet_no)
),
fitting AS (
    SELECT n.*
    FROM wanted n
    JOIN mm_onhand o ON (o.item_id, o.warehouse_id, o.location_id, o.lot_id) = (n.item_id, n.warehouse_id, n.location_id, n.lot_id)
    WHERE o.onhand_qty
          - coalesce((SELECT sum(p.qty) FROM mm_pallet p
                      WHERE (p.item_id, p.warehouse_id, p.location_id, p.lot_id) = (n.item_id, n.warehouse_id, n.location_id, n.lot_id)
                        AND p.status = 'ACTIVE'), 0)
          - (SELECT sum(x.qty) FROM wanted x WHERE x.lot_id = n.lot_id) >= 0
),
pallets AS (
    INSERT INTO mm_pallet (pallet_no, item_id, lot_id, warehouse_id, location_id, qty, created_by, updated_by)
    SELECT pallet_no, item_id, lot_id, warehouse_id, location_id, qty, 'system', 'system' FROM fitting
    ON CONFLICT (pallet_no) DO NOTHING
    RETURNING id, qty
)
INSERT INTO mm_pallet_event (pallet_id, event_type, event_dt, qty, ref_key, created_by, updated_by)
SELECT id, 'CREATE', DATE '2026-11-01', qty, 'DEMO-OPENING', 'system', 'system' FROM pallets;