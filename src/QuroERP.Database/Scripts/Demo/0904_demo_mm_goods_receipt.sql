-- Demo item for the goods receipt screen (--demo only). Fictional item name.
-- One lot-managed Inventory item with a standard purchase price, so a receipt line shows the lot column: a typed lot number
-- or an empty cell (a new lot is numbered). No demo purchase orders or receipts: an order is made and approved on the
-- purchase order screen and received on the goods receipt screen, so stock, lots and PO quantities stay consistent.
-- An applied script is not changed. More demo items go into a new script.

INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, usd_prc, lot_managed, lot_managed_from, created_by, updated_by)
VALUES ('DEMO-RESIN-LOT', 'Demo Resin (Lot)', 'INVENTORY', 'KG', 'Y', 3.2000, true, DATE '2026-01-01', 'system', 'system')
ON CONFLICT (item_cd) DO NOTHING;
