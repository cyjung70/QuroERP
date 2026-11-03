-- PO_STATUS draft, closed and cancelled, and the new names of the legacy values (TASK-MM-005, MM-BR-019)
-- Migrated purchase orders keep PO, which now means approved.

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('DR', 5),
    ('MC', 40),
    ('CN', 50)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'PO_STATUS'
ON CONFLICT DO NOTHING;

INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
SELECT n.id, v.lang_code, v.name, 'system', 'system'
FROM sys_code_minor n
JOIN sys_code_major m ON m.id = n.major_id
JOIN (VALUES
    ('DR', 'ko', '작성중'),
    ('DR', 'en', 'Draft'),
    ('PO', 'ko', '승인'),
    ('PO', 'en', 'Approved'),
    ('RE', 'ko', '부분 입고'),
    ('RE', 'en', 'Partially Received'),
    ('CL', 'ko', '입고 완료'),
    ('CL', 'en', 'Received'),
    ('MC', 'ko', '마감'),
    ('MC', 'en', 'Closed'),
    ('CN', 'ko', '취소'),
    ('CN', 'en', 'Cancelled')
) AS v (minor_code, lang_code, name) ON v.minor_code = n.minor_code
WHERE m.major_code = 'PO_STATUS'
ON CONFLICT (minor_id, lang_code) DO UPDATE
   SET name = EXCLUDED.name, updated_at = now(), updated_by = EXCLUDED.updated_by;
