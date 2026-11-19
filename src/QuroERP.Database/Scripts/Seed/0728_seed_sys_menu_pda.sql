-- PDA screen menus (screens.md "PDA 화면"): receipt and move under purchasing and inventory, issue under sales, each a menu of its
-- own with Full Access for the admin role, as in 0708 (BC-BR-214). The PDA screens save through the PC APIs, which check the PC
-- screen paths, so a role that saves on a PDA screen also needs the PC screen's menu. A code already there is kept as it is;
-- other roles are not assigned.

INSERT INTO sys_menu (menu_code, name, type, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
SELECT v.menu_code, v.name, 'P', v.program_id, v.module, g.id, 'Y', 'system', 'system'
FROM (VALUES ('G2MM08', 'PDA Receipt', '/mm/pda-receipt', 'mm', 'G2MM'),
             ('G2MM09', 'PDA Move', '/mm/pda-move', 'mm', 'G2MM'),
             ('G3SD06', 'PDA Issue', '/sd/pda-issue', 'sd', 'G3SD')) AS v (menu_code, name, program_id, module, group_code)
JOIN sys_menu g ON g.menu_code = v.group_code
ON CONFLICT (menu_code) DO NOTHING;

INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
SELECT r.id, m.id, 'Y', 'system', 'system'
FROM sys_role r
JOIN sys_menu m ON m.menu_code IN ('G2MM08', 'G2MM09', 'G3SD06')
WHERE r.role_code = 'admin'
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu x WHERE x.role_id = r.id AND x.menu_id = m.id);
