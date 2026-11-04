-- Demo warehouses for the warehouse screen (--demo only). Fictional names; one is not in use so the condition shows both.
-- An applied script is not changed. More demo warehouses go into a new script.

INSERT INTO bc_warehouse (warehouse_code, warehouse_name, use_yn, remarks, business_place_id, created_by, updated_by)
SELECT w.code, w.name, w.use_yn, w.remarks, p.id, 'system', 'system'
FROM (VALUES
    ('EAST', 'East Side Storage', true, 'Second stock location'),
    ('OLDYARD', 'Old Yard', false, 'Closed')
) w (code, name, use_yn, remarks)
CROSS JOIN bc_business_place p
WHERE p.is_default
  AND NOT EXISTS (SELECT 1 FROM bc_warehouse x WHERE upper(x.warehouse_code) = w.code);
