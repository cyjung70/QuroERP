-- Demo Set for the Set assembly screen (TASK-SD-073, --demo only). Fictional code and name.
-- A Lot-managed Set item made of the two components that have demo stock (0908): the lot-managed cable reel, whose lots the
-- assembly takes in FIFO order (SD-BR-518, MM-BR-716), and the bolts on Lot '*'. Assembling it makes a new Set Lot linked to the
-- cable reel lots (SD-BR-514, MM-BR-368).
-- It runs once: an existing Set or component row is kept as it is.
-- An applied script is not changed. More demo Sets go into a new script.

INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, so_usd_prc, show_components, lot_managed, lot_managed_from,
                     created_by, updated_by)
VALUES ('DEMO-SET-01', 'Demo Cable Set', 'SET', 'EA', 'Y', 60.0000, true, true, DATE '2026-11-01', 'system', 'system')
ON CONFLICT (item_cd) DO NOTHING;

INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
SELECT i.id, '*', 'system', 'system' FROM bc_item i WHERE i.item_cd = 'DEMO-SET-01'
ON CONFLICT (item_id, lot_no) DO NOTHING;

INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by)
SELECT s.id, v.seq, c.id, v.qty, 'system', 'system'
FROM (VALUES (1, 'DEMO-CABLE-LOT', 1), (2, 'DEMO-BOLT-10', 4)) AS v (seq, child_cd, qty)
JOIN bc_item s ON s.item_cd = 'DEMO-SET-01'
JOIN bc_item c ON c.item_cd = v.child_cd
WHERE NOT EXISTS (SELECT 1 FROM sd_bom b WHERE b.parent_item_id = s.id AND b.seq = v.seq);
