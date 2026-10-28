-- sys_language: default languages are en and ko only (i18n.md, BC localization.md BC-BR-401).
--   sw stays as a row so it can be turned on later, but is no longer in use. A company that already
--   uses sw as its default language keeps it. Numbered after 0103, which adds bc_company.default_lang_code.
-- Applied scripts are never edited. Add a new numbered script for changes.

UPDATE sys_language
SET use_yn = false, updated_at = now(), updated_by = 'system'
WHERE lang_code = 'sw'
  AND use_yn
  AND NOT EXISTS (SELECT 1 FROM bc_company WHERE default_lang_code = 'sw');
