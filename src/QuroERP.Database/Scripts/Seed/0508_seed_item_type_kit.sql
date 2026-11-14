-- ITEM_TYPE KIT: a sales BOM item that expands into its components and keeps no stock (SD-BR-511, MM-BR-007). SET now
-- means an assembled item with stock of its own (SD-BR-513).
-- Applied scripts are never edited. Add a new numbered script for changes.

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, 'KIT', 12, 'system', 'system'
FROM sys_code_major m
WHERE m.major_code = 'ITEM_TYPE';

INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
SELECT n.id, v.lang_code, v.name, 'system', 'system'
FROM sys_code_minor n
JOIN sys_code_major m ON m.id = n.major_id
JOIN (VALUES
    ('ko', '키트'),
    ('en', 'Kit')
) AS v (lang_code, name) ON true
WHERE m.major_code = 'ITEM_TYPE' AND n.minor_code = 'KIT';
