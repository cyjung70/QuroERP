-- sys_app_user.lang_code: 사용자 언어 설정 (i18n.md "언어는 사용자 설정 → 브라우저 언어 → ko", TASK-CORE-004)
--   NULL이면 설정하지 않은 것으로 보고 브라우저 언어를 따른다
--   값은 지원 언어(ko, en)만 허용한다. 언어를 늘리면 새 스크립트로 제약을 바꾼다
-- 적용된 스크립트는 고치지 않는다. 바꿀 것은 새 번호의 스크립트로 추가한다.

ALTER TABLE sys_app_user ADD COLUMN lang_code varchar;
ALTER TABLE sys_app_user ADD CONSTRAINT ck_sys_app_user_lang_code CHECK (lang_code IN ('ko', 'en'));
