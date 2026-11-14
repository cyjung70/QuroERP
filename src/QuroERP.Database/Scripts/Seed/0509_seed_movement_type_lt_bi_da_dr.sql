-- MOVEMENT_TYPE LT: Location transfer inside one warehouse (MM-BR-715); BI: assembly component issue; DA: disassembly set
-- issue; DR: disassembly component receipt (SD-BR-514, SD-BR-516, inventory.md)
-- Applied scripts are never edited. Add a new numbered script for changes.

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m
JOIN (VALUES
    ('LT', 110),
    ('BI', 120),
    ('DA', 130),
    ('DR', 140)
) AS v (minor_code, sort_order) ON true
WHERE m.major_code = 'MOVEMENT_TYPE';

INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
SELECT n.id, v.lang_code, v.name, 'system', 'system'
FROM sys_code_minor n
JOIN sys_code_major m ON m.id = n.major_id
JOIN (VALUES
    ('LT', 'ko', 'Location 이동'),
    ('LT', 'en', 'Location Transfer'),
    ('BI', 'ko', '조립 구성품 출고'),
    ('BI', 'en', 'Assembly Component Issue'),
    ('DA', 'ko', '해체 출고'),
    ('DA', 'en', 'Disassembly Issue'),
    ('DR', 'ko', '해체 구성품 입고'),
    ('DR', 'en', 'Disassembly Component Receipt')
) AS v (minor_code, lang_code, name) ON v.minor_code = n.minor_code
WHERE m.major_code = 'MOVEMENT_TYPE';
