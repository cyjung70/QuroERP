-- Demo sales invoices for the sales invoice screen (--demo only). Fictional names and numbers only.
-- MAPLE HILL SUPPLY (0909) gets two invoices of the demo freight service (0903) in the default warehouse:
--   one open (OP), and one partly paid (PA) by a demo AR receipt of 100.00 applied to it (sd_ar_clear, SD-BR-261).
-- The receipt is written here because the AR receipt screen is not built yet. The numbers take the next DN (SD-BR-106) and
-- CR + yymm (SD-BR-701) numbers and move the counters on, so the screens go on after them.
-- An applied script is not changed. More demo data goes into a new script.

DO $$
DECLARE
    v_customer bigint;
    v_item bigint;
    v_item_name varchar;
    v_tax bigint;
    v_warehouse bigint;
    v_place bigint;
    v_last bigint;
    v_dn_open varchar;
    v_dn_paid varchar;
    v_cr_last bigint;
    v_cr_no varchar;
    v_open bigint;
    v_paid bigint;
    v_receipt bigint;
BEGIN
    SELECT c.id, c.tax_code_id INTO v_customer, v_tax FROM bc_customer c WHERE c.c_name = 'MAPLE HILL SUPPLY';
    SELECT i.id, i.item_name INTO v_item, v_item_name FROM bc_item i WHERE i.item_cd = 'DEMO-FREIGHT';
    SELECT w.id, w.business_place_id INTO v_warehouse, v_place FROM bc_warehouse w WHERE w.is_default ORDER BY w.id LIMIT 1;
    IF v_customer IS NULL OR v_item IS NULL OR v_tax IS NULL OR v_warehouse IS NULL
       OR EXISTS (SELECT 1 FROM sd_sales_invoice d WHERE d.customer_id = v_customer) THEN
        RETURN;
    END IF;

    -- SD-BR-106: DN + 7 digits, after the counter and any number already used
    SELECT greatest(
               coalesce((SELECT substr(n.trans_no, 3)::bigint FROM sys_document_number n
                         WHERE n.trans_type = 'DN' AND n.period = '' AND n.trans_no ~ '^DN[0-9]{1,7}$'), 0),
               coalesce((SELECT max(substr(d.dn_no, 3)::bigint) FROM sd_sales_invoice d WHERE d.dn_no ~ '^DN[0-9]{1,7}$'), 0))
    INTO v_last;
    v_dn_open := 'DN' || lpad((v_last + 1)::text, 7, '0');
    v_dn_paid := 'DN' || lpad((v_last + 2)::text, 7, '0');
    INSERT INTO sys_document_number (trans_type, period, trans_no, created_by, updated_by)
    VALUES ('DN', '', v_dn_paid, 'system', 'system')
    ON CONFLICT (trans_type, period) DO UPDATE SET trans_no = EXCLUDED.trans_no, updated_at = now(), updated_by = 'system';

    INSERT INTO sd_sales_invoice (dn_no, customer_id, dn_dt, ship_dt, due_dt, tot_dn_amt, bal_amt, remarks, terms, type, rep, ship_name,
                                  ship_addr1, ship_city, ship_state, ship_zip_code, ship_country, status, currency_code, exchange_rate,
                                  supply_amt, tax_amt, tot_base_amt, warehouse_id, business_place_id, billing_flow, created_by, updated_by)
    SELECT v_dn_open, c.id, DATE '2026-11-02', DATE '2026-11-02', DATE '2026-12-02', 95, 95, 'Demo open invoice', c.terms, 'IV', c.rep,
           c.ship_name, c.ship_addr1, c.ship_city, c.ship_state, c.ship_zip_code, c.ship_country, 'OP', 'USD', 1, 95, 0, 95,
           v_warehouse, v_place, c.billing_flow, 'system', 'system'
    FROM bc_customer c WHERE c.id = v_customer
    RETURNING id INTO v_open;

    INSERT INTO sd_sales_invoice (dn_no, customer_id, dn_dt, ship_dt, due_dt, tot_dn_amt, bal_amt, remarks, terms, type, rep, ship_name,
                                  ship_addr1, ship_city, ship_state, ship_zip_code, ship_country, status, currency_code, exchange_rate,
                                  supply_amt, tax_amt, tot_base_amt, warehouse_id, business_place_id, billing_flow, created_by, updated_by)
    SELECT v_dn_paid, c.id, DATE '2026-11-03', DATE '2026-11-03', DATE '2026-12-03', 190, 90, 'Demo partly paid invoice', c.terms, 'IV',
           c.rep, c.ship_name, c.ship_addr1, c.ship_city, c.ship_state, c.ship_zip_code, c.ship_country, 'PA', 'USD', 1, 190, 0, 190,
           v_warehouse, v_place, c.billing_flow, 'system', 'system'
    FROM bc_customer c WHERE c.id = v_customer
    RETURNING id INTO v_paid;

    INSERT INTO sd_sales_invoice_line (sales_invoice_id, seq, item_id, item_name, dn_qty, dn_prc, dn_amt, dc_rate, warehouse_id, tax_code_id,
                                       supply_amt, tax_amt, restock, created_by, updated_by)
    VALUES (v_open, 1, v_item, v_item_name, 1, 95, 95, 0, v_warehouse, v_tax, 95, 0, false, 'system', 'system'),
           (v_paid, 1, v_item, v_item_name, 2, 95, 190, 0, v_warehouse, v_tax, 190, 0, false, 'system', 'system');

    -- SD-BR-701: CR + yymm of the receipt date + 6 digits
    SELECT coalesce(max(substr(x.no, 7)::bigint), 0)
    INTO v_cr_last
    FROM (SELECT n.trans_no AS no FROM sys_document_number n WHERE n.trans_type = 'CR' AND n.period = '2611'
          UNION ALL SELECT r.cls_ar_no FROM sd_ar_receipt r) x
    WHERE x.no ~ '^CR2611[0-9]{1,6}$';
    v_cr_no := 'CR2611' || lpad((v_cr_last + 1)::text, 6, '0');
    INSERT INTO sys_document_number (trans_type, period, trans_no, created_by, updated_by)
    VALUES ('CR', '2611', v_cr_no, 'system', 'system')
    ON CONFLICT (trans_type, period) DO UPDATE SET trans_no = EXCLUDED.trans_no, updated_at = now(), updated_by = 'system';

    INSERT INTO sd_ar_receipt (cls_ar_no, customer_id, total_amt, cls_dt, pay_method, remarks, ref_no, receive_amt, currency_code, exchange_rate,
                               business_place_id, created_by, updated_by)
    VALUES (v_cr_no, v_customer, 100, DATE '2026-11-10', 'CHECK', 'Demo receipt', 'DEMO-1001', 100, 'USD', 1, v_place, 'system',
            'system')
    RETURNING id INTO v_receipt;

    INSERT INTO sd_ar_clear (ar_receipt_id, seq, sales_invoice_id, cls_dt, cls_amt, created_by, updated_by)
    VALUES (v_receipt, 1, v_paid, DATE '2026-11-10', 100, 'system', 'system');
END
$$;
