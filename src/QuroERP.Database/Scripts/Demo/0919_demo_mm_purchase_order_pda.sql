-- Demo purchase order for the PDA receipt screen (TASK-MM-060, --demo only). Fictional names.
-- One approved order DEMO-PO-PDA of MAPLE LEAF SUPPLY with nothing received yet: 100 KG of DEMO-RESIN-LOC (lot- and
-- Location-managed, 0915) and 500 EA of DEMO-BOLT-10 (0903) into the default warehouse. Scan or type DEMO-PO-PDA on the PDA,
-- then the item codes. The vendor's tax code is NONE (0901), so the tax is 0. Receiving it is done on the screens, so stock,
-- Lots and PO quantities stay consistent.
-- An applied script is not changed. More demo orders go into a new script.

WITH v AS (
    SELECT v.id AS vendor_id, v.tax_code_id,
           coalesce(v.currency_code, (SELECT c.base_currency_code FROM bc_company c ORDER BY c.id LIMIT 1), 'USD') AS currency_code
    FROM bc_vendor v
    WHERE v.v_name = 'MAPLE LEAF SUPPLY'
),
w AS (SELECT id AS warehouse_id, business_place_id FROM bc_warehouse WHERE is_default LIMIT 1),
po AS (
    INSERT INTO mm_purchase_order (po_no, status, vendor_id, po_dt, currency_code, exchange_rate, due_dt, warehouse_id, remarks,
                                   supply_amt, tax_amt, tot_po_amt, tot_base_amt, revision_no, business_place_id,
                                   created_by, updated_by)
    SELECT 'DEMO-PO-PDA', 'PO', v.vendor_id, DATE '2026-11-02', v.currency_code, 1, DATE '2026-11-30', w.warehouse_id,
           'Demo order for the PDA receipt', 560.0000, 0, 560.0000, 560.0000, 0, w.business_place_id, 'system', 'system'
    FROM v CROSS JOIN w
    ON CONFLICT (po_no) DO NOTHING
    RETURNING id, warehouse_id
)
INSERT INTO mm_purchase_order_line (purchase_order_id, seq, item_id, item_name, po_qty, po_prc, gross_amt, dc_rate, dc_amt, po_amt,
                                    tax_code_id, supply_amt, tax_amt, receipt_qty, due_dt, warehouse_id, created_by, updated_by)
SELECT po.id, l.seq, i.id, i.item_name, l.qty, l.prc, l.qty * l.prc, 0, 0, l.qty * l.prc, v.tax_code_id, l.qty * l.prc, 0, 0,
       DATE '2026-11-30', po.warehouse_id, 'system', 'system'
FROM po
CROSS JOIN v
CROSS JOIN (VALUES (1, 'DEMO-RESIN-LOC', 100.0000, 3.5000), (2, 'DEMO-BOLT-10', 500.0000, 0.4200)) AS l (seq, item_cd, qty, prc)
JOIN bc_item i ON i.item_cd = l.item_cd;
