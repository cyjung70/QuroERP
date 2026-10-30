-- Document currency and tax columns (TASK-CORE-021)
-- Definition: docs/architecture/database-tables.md, localization.md, tax.md
-- Existing rows become migrated data (BC-BR-548): base currency, rate 1, tax code NONE, tax 0, supply = amount.
-- Applied scripts are never edited. Add a new numbered script for changes.

-- Stop before changing anything when rows break the NOT NULL columns the document requires, listing every such row
DO $$
DECLARE
    bad text;
BEGIN
    SELECT string_agg(r, '; ')
      INTO bad
      FROM (SELECT format('bc_alternate_item_group id=%s', id) AS r FROM bc_alternate_item_group
             WHERE alt_item_group_name IS NULL OR class IS NULL
            UNION ALL
            SELECT format('bc_cart_item id=%s', id) FROM bc_cart_item WHERE qty IS NULL
            UNION ALL
            SELECT format('bc_purchase_price id=%s', id) FROM bc_purchase_price WHERE purchase_prc IS NULL) x;
    IF bad IS NOT NULL THEN
        RAISE EXCEPTION 'Rows have NULL in columns that become NOT NULL, fix them first: %', bad;
    END IF;

    -- Documents need the company base currency to fill their document currency
    IF NOT EXISTS (SELECT 1 FROM bc_company)
       AND (EXISTS (SELECT 1 FROM mm_purchase_order) OR EXISTS (SELECT 1 FROM mm_goods_receipt)
            OR EXISTS (SELECT 1 FROM sd_sales_order) OR EXISTS (SELECT 1 FROM sd_sales_invoice)
            OR EXISTS (SELECT 1 FROM sd_ar_receipt) OR EXISTS (SELECT 1 FROM mm_ap_payment)) THEN
        RAISE EXCEPTION 'Documents exist but bc_company does not; set the company country first (--set-country)';
    END IF;
END $$;

ALTER TABLE bc_alternate_item_group ALTER COLUMN alt_item_group_name SET NOT NULL, ALTER COLUMN class SET NOT NULL;
ALTER TABLE bc_cart_item ALTER COLUMN qty SET NOT NULL;
ALTER TABLE bc_purchase_price ALTER COLUMN purchase_prc SET NOT NULL;

-- NONE (no tax) exists for every country (BC-BR-505); migrated lines use it. The install tool keeps existing codes.
INSERT INTO bc_tax_code (tax_code, tax_kind, rate, created_by, updated_by)
SELECT 'NONE', 'NONE', 0, 'system', 'system'
WHERE NOT EXISTS (SELECT 1 FROM bc_tax_code WHERE tax_code = 'NONE');
INSERT INTO bc_tax_code_translation (tax_code_id, lang_code, name, created_by, updated_by)
SELECT t.id, n.lang_code, n.name, 'system', 'system'
FROM bc_tax_code t, (VALUES ('ko', '세금 없음'), ('en', 'No tax')) n(lang_code, name)
WHERE t.tax_code = 'NONE'
ON CONFLICT (tax_code_id, lang_code) DO NOTHING;

-- Customers and vendors: trade currency (NULL = base currency, BC-BR-415), default tax code and KR tax party (BC-BR-521~523)
ALTER TABLE bc_customer
    ADD COLUMN currency_code varchar,
    ADD COLUMN tax_code_id bigint,
    ADD COLUMN tax_party_type varchar,
    ADD COLUMN business_reg_no varchar,
    ADD COLUMN ceo_name varchar,
    ADD COLUMN business_type varchar,
    ADD COLUMN business_item varchar,
    ADD CONSTRAINT fk_bc_customer_tax_code_id FOREIGN KEY (tax_code_id) REFERENCES bc_tax_code (id);
CREATE INDEX ix_bc_customer_tax_code_id ON bc_customer (tax_code_id);

ALTER TABLE bc_vendor
    ADD COLUMN currency_code varchar,
    ADD COLUMN tax_code_id bigint,
    ADD COLUMN tax_party_type varchar,
    ADD COLUMN business_reg_no varchar,
    ADD COLUMN ceo_name varchar,
    ADD COLUMN business_type varchar,
    ADD COLUMN business_item varchar,
    ADD CONSTRAINT fk_bc_vendor_tax_code_id FOREIGN KEY (tax_code_id) REFERENCES bc_tax_code (id);
CREATE INDEX ix_bc_vendor_tax_code_id ON bc_vendor (tax_code_id);

-- Document headers: document currency and rate (BC-BR-412), supply, tax and base currency total (BC-BR-413, BC-BR-541)
ALTER TABLE mm_purchase_order
    ADD COLUMN currency_code varchar,
    ADD COLUMN exchange_rate numeric,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tot_base_amt numeric NOT NULL DEFAULT 0;
UPDATE mm_purchase_order
SET currency_code = (SELECT base_currency_code FROM bc_company ORDER BY id LIMIT 1), exchange_rate = 1,
    supply_amt = coalesce(tot_po_amt, 0), tot_base_amt = coalesce(tot_po_amt, 0);
ALTER TABLE mm_purchase_order
    ALTER COLUMN currency_code SET NOT NULL,
    ALTER COLUMN exchange_rate SET NOT NULL,
    ADD CONSTRAINT ck_mm_purchase_order_exchange_rate CHECK (exchange_rate > 0);

ALTER TABLE mm_goods_receipt
    ADD COLUMN currency_code varchar,
    ADD COLUMN exchange_rate numeric,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tot_base_amt numeric NOT NULL DEFAULT 0;
UPDATE mm_goods_receipt
SET currency_code = (SELECT base_currency_code FROM bc_company ORDER BY id LIMIT 1), exchange_rate = 1,
    supply_amt = coalesce(tot_gr_amt, 0), tot_base_amt = coalesce(tot_gr_amt, 0);
ALTER TABLE mm_goods_receipt
    ALTER COLUMN currency_code SET NOT NULL,
    ALTER COLUMN exchange_rate SET NOT NULL,
    ADD CONSTRAINT ck_mm_goods_receipt_exchange_rate CHECK (exchange_rate > 0);

ALTER TABLE sd_sales_order
    ADD COLUMN currency_code varchar,
    ADD COLUMN exchange_rate numeric,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tot_base_amt numeric NOT NULL DEFAULT 0;
UPDATE sd_sales_order
SET currency_code = (SELECT base_currency_code FROM bc_company ORDER BY id LIMIT 1), exchange_rate = 1,
    supply_amt = coalesce(tot_so_amt, 0), tot_base_amt = coalesce(tot_so_amt, 0);
ALTER TABLE sd_sales_order
    ALTER COLUMN currency_code SET NOT NULL,
    ALTER COLUMN exchange_rate SET NOT NULL,
    ADD CONSTRAINT ck_sd_sales_order_exchange_rate CHECK (exchange_rate > 0);

-- tax_exemption_cert_no: US exemption certificate the invoice was exempted by (BC-BR-525)
ALTER TABLE sd_sales_invoice
    ADD COLUMN currency_code varchar,
    ADD COLUMN exchange_rate numeric,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tot_base_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_exemption_cert_no varchar;
UPDATE sd_sales_invoice
SET currency_code = (SELECT base_currency_code FROM bc_company ORDER BY id LIMIT 1), exchange_rate = 1,
    supply_amt = coalesce(tot_dn_amt, 0), tot_base_amt = coalesce(tot_dn_amt, 0);
ALTER TABLE sd_sales_invoice
    ALTER COLUMN currency_code SET NOT NULL,
    ALTER COLUMN exchange_rate SET NOT NULL,
    ADD CONSTRAINT ck_sd_sales_invoice_exchange_rate CHECK (exchange_rate > 0);

-- Document lines: tax code, supply and tax (BC-BR-541~545)
-- deductible: KR input VAT deducted, false = added to cost (MM-BR-163, MM-BR-164)
ALTER TABLE mm_purchase_order_line
    ADD COLUMN tax_code_id bigint,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD CONSTRAINT fk_mm_purchase_order_line_tax_code_id FOREIGN KEY (tax_code_id) REFERENCES bc_tax_code (id);
UPDATE mm_purchase_order_line
SET tax_code_id = (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), supply_amt = coalesce(po_amt, 0);
ALTER TABLE mm_purchase_order_line ALTER COLUMN tax_code_id SET NOT NULL;
CREATE INDEX ix_mm_purchase_order_line_tax_code_id ON mm_purchase_order_line (tax_code_id);

ALTER TABLE mm_goods_receipt_line
    ADD COLUMN tax_code_id bigint,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN deductible boolean NOT NULL DEFAULT true,
    ADD CONSTRAINT fk_mm_goods_receipt_line_tax_code_id FOREIGN KEY (tax_code_id) REFERENCES bc_tax_code (id);
UPDATE mm_goods_receipt_line
SET tax_code_id = (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), supply_amt = coalesce(gr_amt, 0);
ALTER TABLE mm_goods_receipt_line ALTER COLUMN tax_code_id SET NOT NULL;
CREATE INDEX ix_mm_goods_receipt_line_tax_code_id ON mm_goods_receipt_line (tax_code_id);

ALTER TABLE sd_sales_order_line
    ADD COLUMN tax_code_id bigint,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD CONSTRAINT fk_sd_sales_order_line_tax_code_id FOREIGN KEY (tax_code_id) REFERENCES bc_tax_code (id);
UPDATE sd_sales_order_line
SET tax_code_id = (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), supply_amt = coalesce(so_amt, 0);
ALTER TABLE sd_sales_order_line ALTER COLUMN tax_code_id SET NOT NULL;
CREATE INDEX ix_sd_sales_order_line_tax_code_id ON sd_sales_order_line (tax_code_id);

-- restock: whether a migrated credit memo returned stock; new credit memos are false (SD-BR-205, SD-BR-555)
ALTER TABLE sd_sales_invoice_line
    ADD COLUMN tax_code_id bigint,
    ADD COLUMN supply_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN tax_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN restock boolean NOT NULL DEFAULT true,
    ADD CONSTRAINT fk_sd_sales_invoice_line_tax_code_id FOREIGN KEY (tax_code_id) REFERENCES bc_tax_code (id);
UPDATE sd_sales_invoice_line
SET tax_code_id = (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE'), supply_amt = coalesce(dn_amt, 0);
ALTER TABLE sd_sales_invoice_line ALTER COLUMN tax_code_id SET NOT NULL;
CREATE INDEX ix_sd_sales_invoice_line_tax_code_id ON sd_sales_invoice_line (tax_code_id);

-- Receipts and payments: document currency and rate (BC-BR-412)
ALTER TABLE sd_ar_receipt ADD COLUMN currency_code varchar, ADD COLUMN exchange_rate numeric;
UPDATE sd_ar_receipt SET currency_code = (SELECT base_currency_code FROM bc_company ORDER BY id LIMIT 1), exchange_rate = 1;
ALTER TABLE sd_ar_receipt
    ALTER COLUMN currency_code SET NOT NULL,
    ALTER COLUMN exchange_rate SET NOT NULL,
    ADD CONSTRAINT ck_sd_ar_receipt_exchange_rate CHECK (exchange_rate > 0);

ALTER TABLE mm_ap_payment ADD COLUMN currency_code varchar, ADD COLUMN exchange_rate numeric;
UPDATE mm_ap_payment SET currency_code = (SELECT base_currency_code FROM bc_company ORDER BY id LIMIT 1), exchange_rate = 1;
ALTER TABLE mm_ap_payment
    ALTER COLUMN currency_code SET NOT NULL,
    ALTER COLUMN exchange_rate SET NOT NULL,
    ADD CONSTRAINT ck_mm_ap_payment_exchange_rate CHECK (exchange_rate > 0);

-- Clearings: exchange difference in base currency (BC-BR-414)
ALTER TABLE sd_ar_clear ADD COLUMN fx_diff_base_amt numeric NOT NULL DEFAULT 0;
ALTER TABLE sd_cm_clear ADD COLUMN fx_diff_base_amt numeric NOT NULL DEFAULT 0;
ALTER TABLE mm_ap_clear ADD COLUMN fx_diff_base_amt numeric NOT NULL DEFAULT 0;
