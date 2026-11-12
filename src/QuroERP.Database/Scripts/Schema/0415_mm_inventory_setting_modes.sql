-- mm_inventory_setting: two negative stock modes and the cost adjustment edit switch (TASK-CORE-025)
--   negative_stock_mode: ALLOW is removed. An existing ALLOW row becomes ALLOW_IN_MONTH (MM-BR-003)
--   allow_cost_adjustment_edit: whether a DRAFT cost adjustment line may be edited, default false (MM-BR-420)
-- Definition: database-tables.md (mm_inventory_setting), MM inventory.md, MM closing.md
-- Applied scripts are never edited. Add a new numbered script for changes.

UPDATE mm_inventory_setting
SET negative_stock_mode = 'ALLOW_IN_MONTH', updated_at = now(), updated_by = 'system'
WHERE negative_stock_mode = 'ALLOW';

ALTER TABLE mm_inventory_setting
    DROP CONSTRAINT ck_mm_inventory_setting_negative_stock_mode,
    ADD CONSTRAINT ck_mm_inventory_setting_negative_stock_mode CHECK (negative_stock_mode IN ('ALLOW_IN_MONTH', 'DENY')),
    ADD COLUMN allow_cost_adjustment_edit boolean NOT NULL DEFAULT false;
