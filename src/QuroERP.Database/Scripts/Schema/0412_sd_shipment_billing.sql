-- Shipment billing flows (SD-BR-174~177, SD-BR-143)
--   sd_shipment.billing_flow: fixed when the shipment is first saved (the order's, else the customer's).
--   sd_shipment.sales_invoice_id: the invoice a SHIP_AND_BILL shipment made in the same save (SD-BR-167).
--   sd_shipment_line.tax_code_id: the line tax code the invoice line copies (SD-BR-167, SD-BR-173).
--   sd_shipment_line.lot_id: the lot a lot-managed line issued (MM-BR-355).
--   sd_shipment_line.sales_invoice_line_id: the BILL_THEN_SHIP invoice line a line ships (SD-BR-176).
--   sd_sales_invoice.billing_flow: the flow the invoice belongs to (BILL_THEN_SHIP invoices are billed before shipping).
--   sd_sales_invoice_line.shipped_qty, cancelled_qty: shipped and cancelled qty of a BILL_THEN_SHIP line (SD-BR-143).
-- Migrated shipments were made from their invoice (0303): they are SHIP_AND_BILL and point to the invoice with the same id.

ALTER TABLE sd_shipment
    ADD COLUMN billing_flow varchar NOT NULL DEFAULT 'SHIP_AND_BILL',
    ADD COLUMN sales_invoice_id bigint,
    ADD CONSTRAINT ck_sd_shipment_billing_flow CHECK (billing_flow IN ('SHIP_AND_BILL', 'SHIP_THEN_BILL', 'BILL_THEN_SHIP')),
    ADD CONSTRAINT fk_sd_shipment_sales_invoice_id FOREIGN KEY (sales_invoice_id) REFERENCES sd_sales_invoice (id);
UPDATE sd_shipment s SET sales_invoice_id = s.id
WHERE EXISTS (SELECT 1 FROM sd_sales_invoice i WHERE i.id = s.id);
CREATE INDEX ix_sd_shipment_sales_invoice_id ON sd_shipment (sales_invoice_id);

ALTER TABLE sd_shipment_line
    ADD COLUMN tax_code_id bigint,
    ADD COLUMN lot_id bigint,
    ADD COLUMN sales_invoice_line_id bigint,
    ADD CONSTRAINT fk_sd_shipment_line_tax_code_id FOREIGN KEY (tax_code_id) REFERENCES bc_tax_code (id),
    ADD CONSTRAINT fk_sd_shipment_line_lot_id FOREIGN KEY (lot_id) REFERENCES mm_lot (id),
    ADD CONSTRAINT fk_sd_shipment_line_sales_invoice_line_id FOREIGN KEY (sales_invoice_line_id) REFERENCES sd_sales_invoice_line (id);
UPDATE sd_shipment_line sl SET tax_code_id = l.tax_code_id
FROM sd_sales_invoice_line l
WHERE l.id = sl.id;
UPDATE sd_shipment_line SET tax_code_id = (SELECT id FROM bc_tax_code WHERE tax_code = 'NONE')
WHERE tax_code_id IS NULL;
ALTER TABLE sd_shipment_line ALTER COLUMN tax_code_id SET NOT NULL;
CREATE INDEX ix_sd_shipment_line_tax_code_id ON sd_shipment_line (tax_code_id);
CREATE INDEX ix_sd_shipment_line_lot_id ON sd_shipment_line (lot_id);
CREATE INDEX ix_sd_shipment_line_sales_invoice_line_id ON sd_shipment_line (sales_invoice_line_id);

ALTER TABLE sd_sales_invoice
    ADD COLUMN billing_flow varchar NOT NULL DEFAULT 'SHIP_AND_BILL',
    ADD CONSTRAINT ck_sd_sales_invoice_billing_flow CHECK (billing_flow IN ('SHIP_AND_BILL', 'SHIP_THEN_BILL', 'BILL_THEN_SHIP'));

ALTER TABLE sd_sales_invoice_line
    ADD COLUMN shipped_qty numeric NOT NULL DEFAULT 0,
    ADD COLUMN cancelled_qty numeric NOT NULL DEFAULT 0;

-- SD-BR-164: new shipment numbers (SH + 7 digits) continue after the migrated ones, which took the invoice number digits
INSERT INTO sys_document_number (trans_type, period, trans_no, created_by, updated_by)
SELECT 'SH', '', ship_no, 'system', 'system'
FROM sd_shipment
WHERE ship_no ~ '^SH[0-9]{1,7}$'
ORDER BY substr(ship_no, 3)::bigint DESC
LIMIT 1
ON CONFLICT (trans_type, period) DO NOTHING;
