-- MOVEMENT_TYPE TR: stock transfer between warehouses (inventory.md, MM-BR-606, MM-BR-705)
-- Applied scripts are never edited. Add a new numbered script for changes.

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, 'TR', 80, 'system', 'system'
FROM sys_code_major m
WHERE m.major_code = 'MOVEMENT_TYPE';

INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
SELECT n.id, v.lang_code, v.name, 'system', 'system'
FROM sys_code_minor n
JOIN sys_code_major m ON m.id = n.major_id
JOIN (VALUES
    ('ko', '창고 이동'),
    ('en', 'Stock Transfer')
) AS v (lang_code, name) ON true
WHERE m.major_code = 'MOVEMENT_TYPE' AND n.minor_code = 'TR';
