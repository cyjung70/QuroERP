-- 시연용 화면 메뉴 (--demo일 때만). 지금 있는 화면을 기본 메뉴 그룹 아래에 두고 admin 역할에 Full Access로 배정한다.
-- program_id는 화면 경로다 (app-shell.md 4). 이름은 리소스 Menu.<menu_code>(en·ko)가 정한다.
-- 적용된 스크립트는 고치지 않는다. 화면이 늘면 새 번호의 스크립트로 추가한다.

INSERT INTO sys_menu (menu_code, name, type, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
SELECT v.menu_code, v.name, 'P', v.program_id, v.module, g.id, 'Y', 'system', 'system'
FROM (VALUES
    ('G1BA01', 'Items', '/bc/item', 'bc', 'G1BA'),
    ('G1BA02', 'Codes', '/bc/code', 'bc', 'G1BA'),
    ('G1BA03', 'Major Codes', '/bc/code-major', 'bc', 'G1BA'),
    ('G5AD01', 'Company', '/bc/company', 'bc', 'G5AD'),
    ('G5AD02', 'Currencies', '/bc/currency', 'bc', 'G5AD'),
    ('G5AD03', 'Users', '/bc/user', 'bc', 'G5AD'),
    ('G5AD04', 'Menus', '/bc/company-menu', 'bc', 'G5AD'),
    ('G5AD05', 'Role Menus', '/bc/role-menu', 'bc', 'G5AD')
) AS v (menu_code, name, program_id, module, group_code)
JOIN sys_menu g ON g.menu_code = v.group_code
ON CONFLICT (menu_code) DO NOTHING;

INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
SELECT r.id, m.id, 'Y', 'system', 'system'
FROM sys_role r
JOIN sys_menu m ON m.menu_code IN ('G1BA01', 'G1BA02', 'G1BA03', 'G5AD01', 'G5AD02', 'G5AD03', 'G5AD04', 'G5AD05')
WHERE r.role_code = 'admin'
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu x WHERE x.role_id = r.id AND x.menu_id = m.id);
