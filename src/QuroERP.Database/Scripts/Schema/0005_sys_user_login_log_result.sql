-- sys_user_login_log: 로그인 성공·실패를 남긴다 (security.md "로그", TASK-CORE-003)
--   success:      로그인 성공 여부
--   login_userid: 입력한 사용자 ID. 없는 ID로 실패하면 app_user_id가 비므로 이 값으로 남긴다
--   log_dt UNIQUE를 없앤다. 같은 시각에 여러 로그인이 들어올 수 있다
-- 적용된 스크립트는 고치지 않는다. 바꿀 것은 새 번호의 스크립트로 추가한다.

ALTER TABLE sys_user_login_log ADD COLUMN success boolean NOT NULL DEFAULT false;
ALTER TABLE sys_user_login_log ALTER COLUMN success DROP DEFAULT;
ALTER TABLE sys_user_login_log ADD COLUMN login_userid varchar;
ALTER TABLE sys_user_login_log DROP CONSTRAINT uq_sys_user_login_log_log_dt;
CREATE INDEX ix_sys_user_login_log_log_dt ON sys_user_login_log (log_dt);
