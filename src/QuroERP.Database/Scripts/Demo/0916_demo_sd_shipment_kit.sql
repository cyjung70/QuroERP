-- Demo Kit for the shipment screen's component rows (TASK-CORE-074, --demo only). Fictional code and name.
-- A Kit item with two components that have demo stock (0908): the lot-managed cable reel, whose lots the shipment allocates in
-- FIFO order (SD-BR-518, MM-BR-716), and the bolts on Lot '*'. A saved Kit line shows these allocations under it (SD-BR-517).
-- It runs once: an existing Kit or component row is kept as it is.
-- An applied script is not changed. More demo Kits go into a new script.

INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, so_usd_prc, show_components, created_by, updated_by)
VALUES ('DEMO-KIT-01', 'Demo Cable Kit', 'KIT', 'EA', 'Y', 45.0000, true, 'system', 'system')
ON CONFLICT (item_cd) DO NOTHING;

INSERT INTO mm_lot (item_id, lot_no, created_by, updated_by)
SELECT i.id, '*', 'system', 'system' FROM bc_item i WHERE i.item_cd = 'DEMO-KIT-01'
ON CONFLICT (item_id, lot_no) DO NOTHING;

INSERT INTO sd_bom (parent_item_id, seq, child_item_id, child_qty, created_by, updated_by)
SELECT k.id, v.seq, c.id, v.qty, 'system', 'system'
FROM (VALUES (1, 'DEMO-CABLE-LOT', 1), (2, 'DEMO-BOLT-10', 4)) AS v (seq, child_cd, qty)
JOIN bc_item k ON k.item_cd = 'DEMO-KIT-01'
JOIN bc_item c ON c.item_cd = v.child_cd
WHERE NOT EXISTS (SELECT 1 FROM sd_bom b WHERE b.parent_item_id = k.id AND b.seq = v.seq);
