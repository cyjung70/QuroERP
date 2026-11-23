-- Demo sales order for the PDA shipment screen (TASK-SD-071, --demo only). Fictional names.
-- One open SHIP_AND_BILL order DEMO-SO-PDA of BLUE RIVER MARKET (0905) with nothing shipped yet: 25 EA of DEMO-CABLE-LOT
-- (lot-managed, 0908: lots L2611A 30 and L2611B 20 in the default warehouse, 8 and 3 of them on the demo Pallets of 0917) and
-- 20 EA of DEMO-BOLT-10 (0903, 1000 in stock). Scan or type DEMO-SO-PDA on the PDA, then DEMO-CABLE-LOT: the FIFO suggestion
-- leaves out the Pallets' qty, so it fills 22 from L2611A and 3 from L2611B (two cards); a Pallet label PL2611990001 ships that
-- Pallet whole. Shipping it is done on the screens, so stock, Lots and order quantities stay consistent.
-- An applied script is not changed. More demo orders go into a new script.

WITH c AS (
    SELECT c.id AS customer_id, c.tax_code_id, c.rep, c.terms,
           coalesce(c.currency_code, (SELECT b.base_currency_code FROM bc_company b ORDER BY b.id LIMIT 1), 'USD') AS currency_code
    FROM bc_customer c
    WHERE c.c_name = 'BLUE RIVER MARKET'
),
bp AS (SELECT id AS business_place_id FROM bc_business_place WHERE is_default LIMIT 1),
so AS (
    INSERT INTO sd_sales_order (so_no, status, customer_id, so_dt, due_dt, currency_code, exchange_rate, billing_flow, rep, terms,
                                tot_so_amt, business_place_id, remarks, created_by, updated_by)
    SELECT 'DEMO-SO-PDA', 'SO', c.customer_id, DATE '2026-11-02', DATE '2026-11-30', c.currency_code, 1, 'SHIP_AND_BILL', c.rep,
           c.terms, 518.0000, bp.business_place_id, 'Demo order for the PDA shipment', 'system', 'system'
    FROM c CROSS JOIN bp
    ON CONFLICT (so_no) DO NOTHING
    RETURNING id
)
INSERT INTO sd_sales_order_line (sales_order_id, seq, item_id, so_qty, so_prc, so_amt, dc_rate, tax_code_id, dn_qty,
                                 created_by, updated_by)
SELECT so.id, l.seq, i.id, l.qty, l.prc, l.qty * l.prc, 0, c.tax_code_id, 0, 'system', 'system'
FROM so
CROSS JOIN c
CROSS JOIN (VALUES (1, 'DEMO-CABLE-LOT', 25.0000, 20.0000), (2, 'DEMO-BOLT-10', 20.0000, 0.9000)) AS l (seq, item_cd, qty, prc)
JOIN bc_item i ON i.item_cd = l.item_cd;
