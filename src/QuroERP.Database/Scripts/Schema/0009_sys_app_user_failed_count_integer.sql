-- sys_app_user.failed_count: smallint -> integer (database-tables.md type standard, TASK-BC-001)
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE sys_app_user ALTER COLUMN failed_count TYPE integer;
