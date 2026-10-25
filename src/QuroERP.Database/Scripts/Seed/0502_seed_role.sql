-- 역할 (security.md). AS IS 코드 S700 Menu Role을 role 테이블로 옮긴다 (ADR-0014)
-- 적용된 스크립트는 고치지 않는다. 바꿀 것은 새 번호의 스크립트로 추가한다.

INSERT INTO role (role_code, created_by, updated_by) VALUES
    ('poweruser', 'system', 'system'),
    ('salesman', 'system', 'system'),
    ('admin', 'system', 'system'),
    ('manager', 'system', 'system'),
    ('srep', 'system', 'system');
