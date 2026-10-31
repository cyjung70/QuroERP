-- Company time zone, the base of the business date and of every shown time (TASK-CORE-024)
-- Definition: docs/architecture/database-tables.md (bc_company.time_zone), BC-BR-318, localization.md "시간대"
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE bc_company ADD COLUMN time_zone varchar;

-- Existing companies get the country default (US America/New_York, KR Asia/Seoul)
UPDATE bc_company
   SET time_zone = CASE country_code WHEN 'KR' THEN 'Asia/Seoul' ELSE 'America/New_York' END
 WHERE time_zone IS NULL;

ALTER TABLE bc_company ALTER COLUMN time_zone SET NOT NULL;
