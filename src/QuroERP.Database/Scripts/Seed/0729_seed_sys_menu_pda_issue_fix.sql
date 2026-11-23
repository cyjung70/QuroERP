-- PDA Issue menu repair (TASK-SD-071). Menu code G3SD06 is the PDA Issue screen (0728, resource Menu.G3SD06). A database where
-- G3SD06 already held another program before 0728 ran kept that row, because 0728 does nothing on a code already there, so it
-- has no /sd/pda-issue menu. When no menu opens /sd/pda-issue, G3SD06 is set to the PDA Issue screen under the sales group.
-- Role assignments of the row stay; the admin role gets Full Access as in 0728 (BC-BR-214).

UPDATE sys_menu m
SET name = 'PDA Issue', type = 'P', program_id = '/sd/pda-issue', module = 'sd', parent_menu_id = g.id, valid_flag = 'Y',
    updated_by = 'system', updated_at = now()
FROM sys_menu g
WHERE m.menu_code = 'G3SD06'
  AND g.menu_code = 'G3SD'
  AND m.program_id IS DISTINCT FROM '/sd/pda-issue'
  AND NOT EXISTS (SELECT 1 FROM sys_menu x WHERE x.program_id = '/sd/pda-issue');

INSERT INTO sys_menu (menu_code, name, type, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
SELECT 'G3SD06', 'PDA Issue', 'P', '/sd/pda-issue', 'sd', g.id, 'Y', 'system', 'system'
FROM sys_menu g
WHERE g.menu_code = 'G3SD'
  AND NOT EXISTS (SELECT 1 FROM sys_menu x WHERE x.program_id = '/sd/pda-issue')
ON CONFLICT (menu_code) DO NOTHING;

INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
SELECT r.id, m.id, 'Y', 'system', 'system'
FROM sys_role r
JOIN sys_menu m ON m.menu_code = 'G3SD06' AND m.program_id = '/sd/pda-issue'
WHERE r.role_code = 'admin'
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu x WHERE x.role_id = r.id AND x.menu_id = m.id);
