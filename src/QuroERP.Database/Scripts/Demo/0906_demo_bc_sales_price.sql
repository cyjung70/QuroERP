-- Demo sales prices for the sales price screen (--demo only): two fictional price levels on the demo Inventory items of 0903.
-- The standard sales prices of these items come from 0907. An applied script is not changed; more demo prices go into a new
-- script.

INSERT INTO bc_sales_price (price_level, item_id, sales_prc, created_by, updated_by)
SELECT v.price_level, i.id, v.sales_prc, 'system', 'system'
FROM (VALUES
    ('DLR', 'DEMO-BOLT-10', 0.7500),
    ('DLR', 'DEMO-PANEL-01', 185.0000),
    ('RTL', 'DEMO-BOLT-10', 0.9500)
) AS v (price_level, item_cd, sales_prc)
JOIN bc_item i ON i.item_cd = v.item_cd
ON CONFLICT (price_level, item_id) DO NOTHING;
