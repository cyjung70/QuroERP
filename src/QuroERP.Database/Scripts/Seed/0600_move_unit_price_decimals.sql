-- Moves the unit price decimals of the company country from sys_decimal_setting to bc_country_decimal
--   (BC-BR-422, TASK-CORE-019), then drops sys_decimal_setting.unit_price_decimals.
--   The drop is here, not in Schema, because Seed 0504 still writes that column on a new database
--   and every Schema script runs before Seed. Without a company the US row is used [추정], as in 0504.
-- Applied scripts are never edited. Add a new numbered script for changes.

UPDATE bc_country_decimal d
SET unit_price_decimals = s.unit_price_decimals, updated_at = now(), updated_by = 'system'
FROM sys_decimal_setting s
WHERE d.module = s.module
  AND d.country_code = COALESCE((SELECT country_code FROM bc_company ORDER BY id LIMIT 1), 'US')
  AND d.unit_price_decimals <> s.unit_price_decimals;

ALTER TABLE sys_decimal_setting DROP COLUMN unit_price_decimals;
