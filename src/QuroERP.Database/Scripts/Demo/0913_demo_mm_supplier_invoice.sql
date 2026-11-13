-- Demo supplier invoice for the supplier invoice screen (--demo only). Fictional names and numbers only.
-- One goods receipt of the demo vendor MAPLE LEAF SUPPLY (0901) dated 2026-11-02 with two lines of the demo items (0903), and
-- one open (OP) supplier invoice dated 2026-11-05 that matches the first line in full at the receipt price. The second line
-- stays not invoiced, so "Import Receipts" offers it (MM-BR-763). The vendor buys in the base currency (USD when no company
-- is set up yet) with tax code NONE, so the rate is 1, the tax 0 and no price variance (PV) is posted.
-- The receipt posts its stock like a saved receipt (movement GR, on-hand rows, item totals and moving average cost,
-- MM-BR-111, MM-BR-601) on the default warehouse's Location '*' and the items' Lot '*' (MM-BR-713, MM-BR-363), and the GR
-- and VI numbers come from the same counters the API uses (MM-BR-106, MM-BR-752).
-- It runs once: nothing is added when the demo receipt is already there or a demo input is missing.
-- An applied script is not changed. More demo data goes into a new script.

DO $$
DECLARE
    v_vendor_id bigint;
    v_place_id bigint;
    v_warehouse_id bigint;
    v_location_id bigint;
    v_tax_id bigint;
    v_currency varchar;
    v_bolt_id bigint;
    v_panel_id bigint;
    v_bolt_lot_id bigint;
    v_panel_lot_id bigint;
    v_last varchar;
    v_gr_no varchar;
    v_vi_no varchar;
    v_gr_id bigint;
    v_gr_line_id bigint;
    v_invoice_id bigint;
BEGIN
    SELECT id INTO v_vendor_id FROM bc_vendor WHERE v_name = 'MAPLE LEAF SUPPLY';
    SELECT id INTO v_place_id FROM bc_business_place WHERE is_default;
    SELECT id INTO v_warehouse_id FROM bc_warehouse WHERE is_default;
    SELECT id INTO v_location_id FROM bc_location WHERE warehouse_id = v_warehouse_id AND location_code = '*';
    SELECT id INTO v_tax_id FROM bc_tax_code WHERE tax_code = 'NONE';
    -- The company row comes with --set-country; without it the base currency is USD (like 0303)
    v_currency := coalesce((SELECT base_currency_code FROM bc_company ORDER BY id LIMIT 1), 'USD');
    SELECT id INTO v_bolt_id FROM bc_item WHERE item_cd = 'DEMO-BOLT-10';
    SELECT id INTO v_panel_id FROM bc_item WHERE item_cd = 'DEMO-PANEL-01';
    SELECT id INTO v_bolt_lot_id FROM mm_lot WHERE item_id = v_bolt_id AND lot_no = '*';
    SELECT id INTO v_panel_lot_id FROM mm_lot WHERE item_id = v_panel_id AND lot_no = '*';
    IF v_vendor_id IS NULL OR v_place_id IS NULL OR v_warehouse_id IS NULL OR v_location_id IS NULL OR v_tax_id IS NULL
       OR v_bolt_id IS NULL OR v_panel_id IS NULL OR v_bolt_lot_id IS NULL OR v_panel_lot_id IS NULL
       OR EXISTS (SELECT 1 FROM mm_goods_receipt WHERE vendor_id = v_vendor_id AND ref_no = 'DEMO-PL-2611') THEN
        RETURN;
    END IF;

    -- The next receipt number of 2026-11: GR + yymm + 6 digits after the counter's last number
    SELECT trans_no INTO v_last FROM sys_document_number WHERE trans_type = 'GR' AND period = '2611' FOR UPDATE;
    v_gr_no := 'GR2611' || lpad((coalesce(substr(v_last, 7)::bigint, 0) + 1)::text, 6, '0');
    INSERT INTO sys_document_number (trans_type, period, trans_no, created_by, updated_by)
    VALUES ('GR', '2611', v_gr_no, 'system', 'system')
    ON CONFLICT (trans_type, period) DO UPDATE SET trans_no = excluded.trans_no, updated_at = now(), updated_by = 'system';

    INSERT INTO mm_goods_receipt (gr_no, type, vendor_id, gr_dt, warehouse_id, ref_no, remarks, currency_code, exchange_rate,
                                  supply_amt, tax_amt, tot_gr_amt, tot_base_amt, business_place_id, created_by, updated_by)
    VALUES (v_gr_no, 'ITEM', v_vendor_id, DATE '2026-11-02', v_warehouse_id, 'DEMO-PL-2611', 'Demo receipt for the supplier invoice',
            v_currency, 1, 301.00, 0, 301.00, 301.00, v_place_id, 'system', 'system')
    RETURNING id INTO v_gr_id;

    -- Line 1: 100 bolts at 0.45, invoiced in full below. Line 2: 2 panels at 128.00, not invoiced
    INSERT INTO mm_goods_receipt_line (goods_receipt_id, seq, item_id, item_name, warehouse_id, location_id, lot_id, gr_qty, gr_prc,
                                       gr_amt, tax_code_id, supply_amt, tax_amt, deductible, invoiced_qty, created_by, updated_by)
    VALUES (v_gr_id, 1, v_bolt_id, 'Demo Hex Bolt M10', v_warehouse_id, v_location_id, v_bolt_lot_id, 100, 0.45, 45.00, v_tax_id,
            45.00, 0, true, 100, 'system', 'system')
    RETURNING id INTO v_gr_line_id;
    INSERT INTO mm_goods_receipt_line (goods_receipt_id, seq, item_id, item_name, warehouse_id, location_id, lot_id, gr_qty, gr_prc,
                                       gr_amt, tax_code_id, supply_amt, tax_amt, deductible, created_by, updated_by)
    VALUES (v_gr_id, 2, v_panel_id, 'Demo Steel Panel', v_warehouse_id, v_location_id, v_panel_lot_id, 2, 128.00, 256.00, v_tax_id,
            256.00, 0, true, 'system', 'system');

    -- The receipt's stock: one GR movement per line, the on-hand rows and the item totals and moving average cost
    INSERT INTO mm_goods_movement (document_no, document_dt, mov_type, item_id, warehouse_id, location_id, lot_id, qty, prc, amount,
                                   ref_key, ref_seq, created_by, updated_by)
    VALUES ('DEMO-SI-0001', DATE '2026-11-02', 'GR', v_bolt_id, v_warehouse_id, v_location_id, v_bolt_lot_id, 100, 0.45, 45.00,
            v_gr_no, 1, 'system', 'system'),
           ('DEMO-SI-0002', DATE '2026-11-02', 'GR', v_panel_id, v_warehouse_id, v_location_id, v_panel_lot_id, 2, 128.00, 256.00,
            v_gr_no, 2, 'system', 'system');
    INSERT INTO mm_onhand (item_id, warehouse_id, location_id, lot_id, onhand_qty, created_by, updated_by)
    VALUES (v_bolt_id, v_warehouse_id, v_location_id, v_bolt_lot_id, 100, 'system', 'system'),
           (v_panel_id, v_warehouse_id, v_location_id, v_panel_lot_id, 2, 'system', 'system')
    ON CONFLICT (item_id, warehouse_id, location_id, lot_id) DO UPDATE SET onhand_qty = mm_onhand.onhand_qty + excluded.onhand_qty;
    UPDATE bc_item i
    SET onhand_qty = coalesce(i.onhand_qty, 0) + r.qty,
        stk_val = coalesce(i.stk_val, 0) + r.amount,
        mvg_cost = (coalesce(i.stk_val, 0) + r.amount) / (coalesce(i.onhand_qty, 0) + r.qty),
        updated_by = 'system', updated_at = now()
    FROM (VALUES (v_bolt_id, 100::numeric, 45.00::numeric), (v_panel_id, 2::numeric, 256.00::numeric)) AS r (item_id, qty, amount)
    WHERE i.id = r.item_id;

    -- The next invoice number of 2026-11: VI + yymm + 6 digits
    SELECT trans_no INTO v_last FROM sys_document_number WHERE trans_type = 'VI' AND period = '2611' FOR UPDATE;
    v_vi_no := 'VI2611' || lpad((coalesce(substr(v_last, 7)::bigint, 0) + 1)::text, 6, '0');
    INSERT INTO sys_document_number (trans_type, period, trans_no, created_by, updated_by)
    VALUES ('VI', '2611', v_vi_no, 'system', 'system')
    ON CONFLICT (trans_type, period) DO UPDATE SET trans_no = excluded.trans_no, updated_at = now(), updated_by = 'system';

    INSERT INTO mm_supplier_invoice (invoice_no, type, vendor_id, business_place_id, ref_no, invoice_dt, due_dt, terms, currency_code,
                                     exchange_rate, supply_amt, tax_amt, tot_amt, tot_base_amt, bal_amt, status, tax_invoice_received,
                                     remarks, created_by, updated_by)
    VALUES (v_vi_no, 'INV', v_vendor_id, v_place_id, 'MLS-INV-1001', DATE '2026-11-05', DATE '2026-12-05', 'NET_30', v_currency,
            1, 45.00, 0, 45.00, 45.00, 45.00, 'OP', true, 'Demo invoice for the bolts', 'system', 'system')
    RETURNING id INTO v_invoice_id;
    INSERT INTO mm_supplier_invoice_line (supplier_invoice_id, seq, goods_receipt_line_id, item_id, qty, prc, tax_code_id, supply_amt,
                                          tax_amt, deductible, pv_amt, created_by, updated_by)
    VALUES (v_invoice_id, 1, v_gr_line_id, v_bolt_id, 100, 0.45, v_tax_id, 45.00, 0, true, 0, 'system', 'system');
END
$$;
