-- Warehouse type and the consignment warehouse (TASK-CORE-029)
-- Definition: database-tables.md bc_warehouse, MM warehouse.md (MM-BR-709~711), inventory.md
--   New:  bc_warehouse.warehouse_type NORMAL or CONSIGNMENT. Existing warehouses are NORMAL.
--         One consignment warehouse CONSIGN / Consignment in the default business place, made here for new and existing
--         databases alike (installation runs these scripts). It holds billed but unshipped stock.
--   Keys: at most one consignment warehouse, and it is never the default warehouse.
--   A warehouse already coded CONSIGN stops this script on uq_bc_warehouse_warehouse_code; rename it first.
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE bc_warehouse
    ADD COLUMN warehouse_type varchar NOT NULL DEFAULT 'NORMAL',
    ADD CONSTRAINT ck_bc_warehouse_warehouse_type CHECK (warehouse_type IN ('NORMAL', 'CONSIGNMENT')),
    ADD CONSTRAINT ck_bc_warehouse_consignment_not_default CHECK (warehouse_type <> 'CONSIGNMENT' OR NOT is_default);

CREATE UNIQUE INDEX uq_bc_warehouse_single_consignment ON bc_warehouse (warehouse_type) WHERE warehouse_type = 'CONSIGNMENT';

INSERT INTO bc_warehouse (warehouse_code, warehouse_name, warehouse_type, is_default, use_yn, business_place_id, created_by, updated_by)
SELECT 'CONSIGN', 'Consignment', 'CONSIGNMENT', false, true, p.id, 'system', 'system'
FROM bc_business_place p
WHERE p.is_default;
