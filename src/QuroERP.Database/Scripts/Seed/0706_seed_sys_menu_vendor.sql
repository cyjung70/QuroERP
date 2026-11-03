-- Screen menu for the vendor master screen (app-shell.md 3.2), with Full Access for the admin role, as in 0701.
-- A code already there is kept as it is; other roles are not assigned.

INSERT INTO sys_menu (menu_code, name, type, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
SELECT 'G1BA10', 'Vendors', 'P', '/mm/vendor', 'mm', g.id, 'Y', 'system', 'system'
FROM sys_menu g
WHERE g.menu_code = 'G1BA'
ON CONFLICT (menu_code) DO NOTHING;

INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
SELECT r.id, m.id, 'Y', 'system', 'system'
FROM sys_role r
JOIN sys_menu m ON m.menu_code = 'G1BA10'
WHERE r.role_code = 'admin'
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu x WHERE x.role_id = r.id AND x.menu_id = m.id);
