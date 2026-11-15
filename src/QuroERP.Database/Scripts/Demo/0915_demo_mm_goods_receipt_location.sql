-- Demo item for the goods receipt screen's Location, Lot attribute and Pallet columns (TASK-CORE-073, --demo only). Fictional
-- item name. One lot-managed and Location-managed Inventory item with a standard purchase price: a receipt line takes a
-- Location of the warehouse (the demo shelves of 0914), the supplier Lot number and the dates, and a Pallet qty.
-- An applied script is not changed. More demo items go into a new script.

INSERT INTO bc_item (item_cd, item_name, type, unit, valid_flag, usd_prc, lot_managed, lot_managed_from, location_managed,
                     created_by, updated_by)
VALUES ('DEMO-RESIN-LOC', 'Demo Resin (Lot, Location)', 'INVENTORY', 'KG', 'Y', 3.5000, true, DATE '2026-01-01', true,
        'system', 'system')
ON CONFLICT (item_cd) DO NOTHING;
