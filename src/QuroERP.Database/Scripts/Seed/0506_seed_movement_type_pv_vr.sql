-- MOVEMENT_TYPE PV: supplier invoice price difference, qty 0 and amount only; VR: vendor return issue at the original
-- receipt price (inventory.md, MM-BR-606, MM-BR-759, MM-BR-785)
-- Applied scripts are never edited. Add a new numbered script for changes.

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m
JOIN (VALUES
    ('PV', 90),
    ('VR', 100)
) AS v (minor_code, sort_order) ON true
WHERE m.major_code = 'MOVEMENT_TYPE';

INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
SELECT n.id, v.lang_code, v.name, 'system', 'system'
FROM sys_code_minor n
JOIN sys_code_major m ON m.id = n.major_id
JOIN (VALUES
    ('PV', 'ko', '송장 단가 차이'),
    ('PV', 'en', 'Invoice Price Variance'),
    ('VR', 'ko', '공급자 반품'),
    ('VR', 'en', 'Vendor Return')
) AS v (minor_code, lang_code, name) ON v.minor_code = n.minor_code
WHERE m.major_code = 'MOVEMENT_TYPE';
