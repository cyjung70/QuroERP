-- 역할 (security.md). AS IS 코드 S700 Menu Role을 sys_role로 옮긴다 (codes.md 코드가 아닌 것). sort_order = AS IS seq
-- 적용된 스크립트는 고치지 않는다. 바꿀 것은 새 번호의 스크립트로 추가한다.

INSERT INTO sys_role (role_code, sort_order, created_by, updated_by) VALUES
    ('poweruser', 10, 'system', 'system'),
    ('salesman', 20, 'system', 'system'),
    ('admin', 30, 'system', 'system'),
    ('manager', 40, 'system', 'system'),
    ('srep', 50, 'system', 'system');
