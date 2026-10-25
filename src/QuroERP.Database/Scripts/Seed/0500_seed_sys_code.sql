-- 코드 Major·Minor (codes.md)
-- Major: glossary.md 코드 목록 + MOVEMENT_TYPE(inventory.md)
-- Minor: AS IS B_MINOR(QuroHarness mapping/codes.md)의 시스템·일반 코드만 넣는다.
--   업무 데이터(ITEM_CLASS, GRADE, PLATING, OFFSET, SALES_REP, DISCOUNT_STATUS)와
--   MVP 밖(AP_PAYMENT_METHOD, AR_PAYMENT_METHOD)의 Minor는 데이터 이관 때 넣는다.
-- 적용된 스크립트는 고치지 않는다. 바꿀 것은 새 번호의 스크립트로 추가한다.

INSERT INTO sys_code_major (major_code, created_by, updated_by) VALUES
    ('AP_PAYMENT_METHOD', 'system', 'system'),
    ('AR_PAYMENT_METHOD', 'system', 'system'),
    ('CART_NO', 'system', 'system'),
    ('CREDIT_MEMO_STATUS', 'system', 'system'),
    ('DEPARTMENT', 'system', 'system'),
    ('DISCOUNT_STATUS', 'system', 'system'),
    ('DISPLAY_OPTIONS', 'system', 'system'),
    ('FREIGHT_CHARGE', 'system', 'system'),
    ('GRADE', 'system', 'system'),
    ('INVENTORY_ADJUST', 'system', 'system'),
    ('INVOICE_STATUS', 'system', 'system'),
    ('ITEM_CLASS', 'system', 'system'),
    ('ITEM_TYPE', 'system', 'system'),
    ('OFFSET', 'system', 'system'),
    ('PLATING', 'system', 'system'),
    ('PO_STATUS', 'system', 'system'),
    ('SALES_REP', 'system', 'system'),
    ('SHIPMENT_VIA', 'system', 'system'),
    ('SO_STATUS', 'system', 'system'),
    ('STATE', 'system', 'system'),
    ('TERMS', 'system', 'system'),
    ('UNIT', 'system', 'system'),
    ('VALID_FLAG', 'system', 'system'),
    ('MOVEMENT_TYPE', 'system', 'system');

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('1', 10),
    ('2', 20),
    ('3', 30),
    ('4', 40),
    ('5', 50)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'CART_NO';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('OP', 10),
    ('PA', 20),
    ('CL', 30),
    ('VO', 40)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'CREDIT_MEMO_STATUS';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('SALES', 10),
    ('PURCHASE', 20)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'DEPARTMENT';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('Y', 10),
    ('N', 20),
    ('ALL', 30)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'DISPLAY_OPTIONS';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('Y', 1),
    ('N', 2),
    ('OWNED_ACCT', 3),
    ('HALF', 4),
    ('Y_NCOD', 5)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'FREIGHT_CHARGE';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('REGULAR', 10),
    ('JUNK', 20)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'INVENTORY_ADJUST';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('OP', 10),
    ('PA', 20),
    ('CL', 30),
    ('VO', 40)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'INVOICE_STATUS';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('INVENTORY', 10),
    ('SET', 11),
    ('SERVICE', 21)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'ITEM_TYPE';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('PO', 10),
    ('RE', 20),
    ('CL', 30)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'PO_STATUS';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('DELIVERY', 10),
    ('UPS', 20),
    ('UCL', 30),
    ('FEDEX', 40),
    ('TRUCKING', 50),
    ('PICKUP', 60)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'SHIPMENT_VIA';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('SO', 10),
    ('RE', 20),
    ('CL', 30)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'SO_STATUS';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('AL', 10),
    ('AK', 20),
    ('AZ', 30),
    ('AR', 40),
    ('CA', 50),
    ('CO', 60),
    ('CT', 70),
    ('DE', 80),
    ('DC', 90),
    ('FL', 100),
    ('GA', 110),
    ('HI', 120),
    ('ID', 130),
    ('IL', 140),
    ('IN', 150),
    ('IA', 160),
    ('KS', 170),
    ('KY', 180),
    ('LA', 190),
    ('ME', 200),
    ('MD', 210),
    ('MA', 220),
    ('MI', 230),
    ('MN', 240),
    ('MS', 250),
    ('MO', 260),
    ('MT', 270),
    ('NE', 280),
    ('NV', 290),
    ('NH', 300),
    ('NJ', 310),
    ('NM', 320),
    ('NY', 330),
    ('NC', 340),
    ('ND', 350),
    ('OH', 360),
    ('OK', 370),
    ('OR', 380),
    ('PA', 390),
    ('RI', 400),
    ('SC', 410),
    ('SD', 420),
    ('TN', 430),
    ('TX', 440),
    ('UT', 450),
    ('VT', 460),
    ('VA', 470),
    ('WA', 480),
    ('WV', 490),
    ('WI', 500),
    ('WY', 510)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'STATE';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('COD_15', 10),
    ('COD_30', 20),
    ('COD_45', 30),
    ('COD_60', 40),
    ('COD_90', 50),
    ('COD_CHECK', 60),
    ('COD_M_O', 70),
    ('DUE_ON_RECEIPT', 80),
    ('NET_15', 90),
    ('NET_30', 100),
    ('NET_60', 110),
    ('NET_45', 111),
    ('PRE_PAID', 120)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'TERMS';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('PCS', 10),
    ('EA', 20),
    ('G', 30),
    ('DZ', 40),
    ('CM', 50),
    ('PAIR', 60),
    ('PK', 70)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'UNIT';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('Y', 10),
    ('N', 20),
    ('S', 30)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'VALID_FLAG';

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m CROSS JOIN (VALUES
    ('GR', 10),
    ('DN', 20),
    ('GI', 30),
    ('RT', 40),
    ('UR', 50),
    ('CT', 60),
    ('BP', 70)
) AS v (minor_code, sort_order)
WHERE m.major_code = 'MOVEMENT_TYPE';
