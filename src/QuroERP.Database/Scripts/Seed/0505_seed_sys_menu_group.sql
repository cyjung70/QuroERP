-- 기본 메뉴 그룹 (app-shell.md 3.1). 사이드 메뉴의 최상위 그룹이고, 이름은 리소스 Menu.<menu_code>(en·ko)가 정한다.
-- 회사 메뉴 화면에서 이미 같은 코드를 만들었으면 그대로 둔다.
-- 적용된 스크립트는 고치지 않는다. 바꿀 것은 새 번호의 스크립트로 추가한다.

INSERT INTO sys_menu (menu_code, name, type, module, valid_flag, created_by, updated_by) VALUES
    ('G1BA', 'Master Data', 'M', 'bc', 'Y', 'system', 'system'),
    ('G2MM', 'Purchasing & Inventory', 'M', 'mm', 'Y', 'system', 'system'),
    ('G3SD', 'Sales', 'M', 'sd', 'Y', 'system', 'system'),
    ('G4RP', 'Reports', 'M', 'rpt', 'Y', 'system', 'system'),
    ('G5AD', 'Administration', 'M', 'bc', 'Y', 'system', 'system')
ON CONFLICT (menu_code) DO NOTHING;
