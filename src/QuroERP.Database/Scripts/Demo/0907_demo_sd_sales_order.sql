-- Demo data for the sales order screen (--demo only). Fictional codes and names only.
-- Standard sales prices and a barcode for the demo items (0903), a price-level price for BLUE RIVER MARKET (0905, its price
-- level is its name), two discount codes and a discount status for GREEN VALLEY STORES, so a new line shows the level price,
-- the standard price and a discount. No demo sales orders: they are made on the screen.
-- An applied script is not changed. More demo data goes into a new script.

UPDATE bc_item i
SET so_usd_prc = v.so_usd_prc, upc_code = COALESCE(i.upc_code, v.upc_code), updated_by = 'system', updated_at = now()
FROM (VALUES
    ('DEMO-BOLT-10', 0.9000, '200000000017'),
    ('DEMO-PANEL-01', 210.0000, '200000000024'),
    ('DEMO-FREIGHT', 95.0000, NULL)
) AS v (item_cd, so_usd_prc, upc_code)
WHERE i.item_cd = v.item_cd
  AND i.so_usd_prc IS NULL
  AND NOT EXISTS (SELECT 1 FROM bc_item o WHERE o.upc_code = v.upc_code AND o.id <> i.id);

INSERT INTO bc_sales_price (price_level, item_id, sales_prc, created_by, updated_by)
SELECT c.price_level, i.id, 195.0000, 'system', 'system'
FROM bc_customer c
JOIN bc_item i ON i.item_cd = 'DEMO-PANEL-01'
WHERE c.c_name = 'BLUE RIVER MARKET' AND c.price_level IS NOT NULL
ON CONFLICT (price_level, item_id) DO NOTHING;

INSERT INTO sys_code_minor (major_id, minor_code, sort_order, created_by, updated_by)
SELECT m.id, v.minor_code, v.sort_order, 'system', 'system'
FROM sys_code_major m
CROSS JOIN (VALUES ('DEMO_5', 1), ('DEMO_10', 2)) AS v (minor_code, sort_order)
WHERE m.major_code = 'DISCOUNT_STATUS'
  AND NOT EXISTS (SELECT 1 FROM sys_code_minor n WHERE n.major_id = m.id AND n.minor_code = v.minor_code);

INSERT INTO sys_code_minor_translation (minor_id, lang_code, name, created_by, updated_by)
SELECT n.id, v.lang_code, v.name, 'system', 'system'
FROM sys_code_minor n
JOIN sys_code_major m ON m.id = n.major_id AND m.major_code = 'DISCOUNT_STATUS'
JOIN (VALUES
    ('DEMO_5', 'en', 'Demo 5% off'),
    ('DEMO_5', 'ko', '데모 5% 할인'),
    ('DEMO_10', 'en', 'Demo 10% off'),
    ('DEMO_10', 'ko', '데모 10% 할인')
) AS v (minor_code, lang_code, name) ON v.minor_code = n.minor_code
WHERE NOT EXISTS (SELECT 1 FROM sys_code_minor_translation t WHERE t.minor_id = n.id AND t.lang_code = v.lang_code);

INSERT INTO sd_discount_rate (discount_code, rate, sort_order, created_by, updated_by)
VALUES ('DEMO_5', -5, 1, 'system', 'system'), ('DEMO_10', -10, 2, 'system', 'system')
ON CONFLICT (discount_code) DO NOTHING;

UPDATE bc_customer
SET dc_status = 'DEMO_5', updated_by = 'system', updated_at = now()
WHERE c_name = 'GREEN VALLEY STORES' AND dc_status IS NULL;
