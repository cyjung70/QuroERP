-- Demo items and vendor prices for the purchase order screen (--demo only). Fictional item names.
-- Two Inventory items and one Service item, each with a standard purchase price (usd_prc). The price group of the demo
-- vendor MAPLE LEAF SUPPLY (0901) has its own price for the first item, so picking it fills the group price; the others
-- fill the standard price. No demo purchase orders: they are made on the screen.
-- An applied script is not changed. More demo items go into a new script.

INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, usd_prc, created_by, updated_by)
SELECT i.item_cd, i.item_name, i.type, i.unit, 'Y', i.usd_prc, 'system', 'system'
FROM (VALUES
    ('DEMO-BOLT-10', 'Demo Hex Bolt M10', 'INVENTORY', 'EA', 0.4500),
    ('DEMO-PANEL-01', 'Demo Steel Panel', 'INVENTORY', 'EA', 128.0000),
    ('DEMO-FREIGHT', 'Demo Freight Service', 'SERVICE', 'LOT', 75.0000)
) AS i (item_cd, item_name, type, unit, usd_prc)
ON CONFLICT (item_cd) DO NOTHING;

INSERT INTO bc_purchase_price (vendor_price, item_id, purchase_prc, created_by, updated_by)
SELECT v.vendor_price, i.id, 0.4200, 'system', 'system'
FROM bc_vendor v
JOIN bc_item i ON i.item_cd = 'DEMO-BOLT-10'
WHERE v.v_name = 'MAPLE LEAF SUPPLY' AND v.vendor_price IS NOT NULL
ON CONFLICT (vendor_price, item_id) DO NOTHING;
