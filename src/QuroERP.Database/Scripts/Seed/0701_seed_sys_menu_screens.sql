-- Screen menus for the screen paths that guard APIs (app-shell.md 3.2, security.md "권한"), under the default menu groups (3.1),
-- with Full Access for the admin role. Names come from the resources Menu.<menu_code> (en, ko).
-- Codes already there (the demo screen menus, or a company's own) are kept as they are; other roles are not assigned.
-- Applied scripts are not changed. Later screens add their menus in a new script.

INSERT INTO sys_menu (menu_code, name, type, program_id, module, parent_menu_id, valid_flag, created_by, updated_by)
SELECT v.menu_code, v.name, 'P', v.program_id, v.module, g.id, 'Y', 'system', 'system'
FROM (VALUES
    ('G1BA01', 'Items', '/bc/item', 'bc', 'G1BA'),
    ('G1BA02', 'Codes', '/bc/code', 'bc', 'G1BA'),
    ('G1BA03', 'Major Codes', '/bc/code-major', 'bc', 'G1BA'),
    ('G1BA04', 'Item Barcodes', '/bc/item-barcode', 'bc', 'G1BA'),
    ('G1BA05', 'Purchase Prices', '/bc/purchase-price', 'bc', 'G1BA'),
    ('G2MM01', 'Purchase Orders', '/mm/purchase-order', 'mm', 'G2MM'),
    ('G5AD01', 'Company', '/bc/company', 'bc', 'G5AD'),
    ('G5AD02', 'Currencies', '/bc/currency', 'bc', 'G5AD'),
    ('G5AD03', 'Users', '/bc/user', 'bc', 'G5AD'),
    ('G5AD04', 'Menus', '/bc/company-menu', 'bc', 'G5AD'),
    ('G5AD05', 'Role Menus', '/bc/role-menu', 'bc', 'G5AD'),
    ('G5AD06', 'Exchange Rates', '/bc/exchange-rate', 'bc', 'G5AD'),
    ('G5AD07', 'Tax Codes', '/bc/tax-code', 'bc', 'G5AD'),
    ('G5AD08', 'Tax Areas', '/bc/tax-area', 'bc', 'G5AD'),
    ('G5AD09', 'Localization Setting', '/bc/localization-setting', 'bc', 'G5AD')
) AS v (menu_code, name, program_id, module, group_code)
JOIN sys_menu g ON g.menu_code = v.group_code
ON CONFLICT (menu_code) DO NOTHING;

INSERT INTO sys_role_menu (role_id, menu_id, full_access, created_by, updated_by)
SELECT r.id, m.id, 'Y', 'system', 'system'
FROM sys_role r
JOIN sys_menu m ON m.menu_code IN ('G1BA01', 'G1BA02', 'G1BA03', 'G1BA04', 'G1BA05', 'G2MM01', 'G5AD01', 'G5AD02', 'G5AD03',
    'G5AD04', 'G5AD05', 'G5AD06', 'G5AD07', 'G5AD08', 'G5AD09')
WHERE r.role_code = 'admin'
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu x WHERE x.role_id = r.id AND x.menu_id = m.id);
