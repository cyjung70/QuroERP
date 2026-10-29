-- mm_inventory_setting: one global setting with three negative stock modes (MM-BR-003, inventory.md)
--   Before: company_id, plant_id, warehouse_id, allow_negative_stock (per company/plant/warehouse)
--   After:  id, negative_stock_mode (ALLOW, ALLOW_IN_MONTH, DENY), audit columns. Exactly one row
--   Existing rows: allow_negative_stock true -> ALLOW, false -> DENY. The company-level row is kept
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE mm_inventory_setting ADD COLUMN negative_stock_mode varchar NOT NULL DEFAULT 'DENY';

UPDATE mm_inventory_setting
SET negative_stock_mode = CASE WHEN allow_negative_stock THEN 'ALLOW' ELSE 'DENY' END;

-- Keep one row: the company-level setting (no plant, no warehouse) first, then the lowest id
DELETE FROM mm_inventory_setting
WHERE id <> (
    SELECT id FROM mm_inventory_setting
    ORDER BY plant_id NULLS FIRST, warehouse_id NULLS FIRST, id
    LIMIT 1);

ALTER TABLE mm_inventory_setting DROP CONSTRAINT uq_mm_inventory_setting_company_id_plant_id_warehouse_id;
ALTER TABLE mm_inventory_setting DROP CONSTRAINT fk_mm_inventory_setting_company_id;
ALTER TABLE mm_inventory_setting DROP COLUMN company_id;
ALTER TABLE mm_inventory_setting DROP COLUMN plant_id;
ALTER TABLE mm_inventory_setting DROP COLUMN warehouse_id;
ALTER TABLE mm_inventory_setting DROP COLUMN allow_negative_stock;

ALTER TABLE mm_inventory_setting ADD CONSTRAINT ck_mm_inventory_setting_negative_stock_mode
    CHECK (negative_stock_mode IN ('ALLOW', 'ALLOW_IN_MONTH', 'DENY'));

-- Only one row may exist
CREATE UNIQUE INDEX uq_mm_inventory_setting_single_row ON mm_inventory_setting ((true));

-- The setting always exists; the default is DENY
INSERT INTO mm_inventory_setting (negative_stock_mode, created_by, updated_by)
SELECT 'DENY', 'system', 'system'
WHERE NOT EXISTS (SELECT 1 FROM mm_inventory_setting);
