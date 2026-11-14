-- Demo Locations (shelves) for the Location screen (--demo only). Fictional codes and names.
-- The default warehouse gets two shelves in use and one not in use, so the screen shows both; the demo warehouse EAST (0902)
-- gets one. Every warehouse keeps its system Location '*' (MM-BR-712): a warehouse added by a script without it gets it here.
-- It runs once per row: a code already in the warehouse is kept as it is.
-- An applied script is not changed. More demo Locations go into a new script.

INSERT INTO bc_location (warehouse_id, location_code, is_system, created_by, updated_by)
SELECT w.id, '*', true, 'system', 'system'
FROM bc_warehouse w
ON CONFLICT (warehouse_id, location_code) DO NOTHING;

INSERT INTO bc_location (warehouse_id, location_code, location_name, use_yn, remarks, created_by, updated_by)
SELECT w.id, l.code, l.name, l.use_yn, l.remarks, 'system', 'system'
FROM (VALUES
    (NULL, 'A-01', 'Rack A shelf 1', true, NULL),
    (NULL, 'A-02', 'Rack A shelf 2', true, NULL),
    (NULL, 'B-01', 'Rack B shelf 1', false, 'Rack taken down'),
    ('EAST', 'E-01', 'East rack 1', true, NULL)
) l (warehouse_code, code, name, use_yn, remarks)
JOIN bc_warehouse w ON (l.warehouse_code IS NULL AND w.is_default) OR upper(w.warehouse_code) = l.warehouse_code
ON CONFLICT (warehouse_id, location_code) DO NOTHING;
