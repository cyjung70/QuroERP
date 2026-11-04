-- Demo customers for the customer screen (--demo only). Fictional names, 555 phone numbers and example.com mail only.
-- Sales reps are business data the seed leaves empty, so two demo reps are added. One customer is inactive and one has a
-- state tax exemption certificate (BC-BR-524). An applied script is not changed; more demo customers go into a new script.

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m
CROSS JOIN (VALUES ('DEMO_A', 1), ('DEMO_B', 2)) AS v (minor_code, sort_order)
WHERE m.major_code = 'SALES_REP'
  AND NOT EXISTS (SELECT 1 FROM sys_code_minor n WHERE n.major_id = m.id AND n.minor_code = v.minor_code);

INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
SELECT n.id, v.lang_code, v.name, 'system', 'system'
FROM sys_code_minor n
JOIN sys_code_major m ON m.id = n.major_id AND m.major_code = 'SALES_REP'
JOIN (VALUES
    ('DEMO_A', 'en', 'Alex Morgan'),
    ('DEMO_A', 'ko', 'Alex Morgan'),
    ('DEMO_B', 'en', 'Jordan Lee'),
    ('DEMO_B', 'ko', 'Jordan Lee')
) AS v (minor_code, lang_code, name) ON v.minor_code = n.minor_code
WHERE NOT EXISTS (SELECT 1 FROM sys_code_minor_translation t WHERE t.minor_id = n.id AND t.lang_code = v.lang_code);

INSERT INTO bc_customer (c_name, ship_name, contact, phone1, email, rep, price_level, terms, valid_flag, remarks, addr1, city, state,
                         country, zip_code, ship_addr1, ship_city, ship_state, ship_country, ship_zip_code, credit_limit, start_dt,
                         tax_code_id, created_by, updated_by)
SELECT v.c_name, v.c_name, v.contact, v.phone1, v.email, v.rep, v.c_name, v.terms, v.valid_flag, v.remarks, v.addr1, v.city, v.state,
       'USA', v.zip_code, v.addr1, v.city, v.state, 'USA', v.zip_code, v.credit_limit, DATE '2026-01-05', t.id, 'system', 'system'
FROM (VALUES
    ('BLUE RIVER MARKET', 'Chris Hall', '555-0201', 'buyer@blueriver.example.com', 'DEMO_A', 'NET_30', 'Y', 'Weekly orders',
     '10 Sample Way', 'Springfield', 'CA', '90010', 5000),
    ('GREEN VALLEY STORES', 'Pat Young', '555-0202', 'ap@greenvalley.example.com', 'DEMO_B', 'COD_CHECK', 'Y', NULL,
     '20 Demo Lane', 'Riverside', 'NY', '10010', 0),
    ('SUNSET OUTLET', 'Robin Fox', '555-0203', NULL, 'DEMO_A', 'NET_30', 'N', 'No longer used',
     '30 Test Court', 'Lakeside', 'CA', '90020', 0)
) AS v (c_name, contact, phone1, email, rep, terms, valid_flag, remarks, addr1, city, state, zip_code, credit_limit)
LEFT JOIN bc_tax_code t ON t.tax_code = 'NONE'
ON CONFLICT (c_name) DO NOTHING;

INSERT INTO sd_customer_tax_exemption (customer_id, certificate_no, state_code, valid_to, created_by, updated_by)
SELECT c.id, 'EX-DEMO-0001', 'CA', DATE '2027-12-31', 'system', 'system'
FROM bc_customer c
WHERE c.c_name = 'BLUE RIVER MARKET'
  AND NOT EXISTS (SELECT 1 FROM sd_customer_tax_exemption x WHERE x.customer_id = c.id);
