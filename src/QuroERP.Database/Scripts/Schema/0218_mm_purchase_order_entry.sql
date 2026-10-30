-- PO Entry columns on mm_purchase_order and mm_purchase_order_line (database-tables.md, purchase-order.md)
--   header: incoterms (MM-BR-027), due date and receiving warehouse defaults for lines (MM-BR-026),
--           payment terms and buyer text for the PO form, revision_no (MM-BR-021),
--           approve / close / cancel time and user (MM-BR-020, MM-BR-022, MM-BR-023)
--   line:   gross_amt, dc_rate, dc_amt with po_amt = gross_amt - dc_amt (MM-BR-015, MM-BR-025),
--           due date and warehouse (MM-BR-026), purchase request line it came from (MM-BR-554, MM-BR-555)
-- Vendor stays vendor_id → bc_vendor.id (the AS IS key was v_name). Document currency and tax columns are a separate
-- migration (BC-BR-412, BC-BR-541). mm_purchase_request_line does not exist yet, so purchase_request_line_id gets its
-- foreign key in the purchase request migration.

ALTER TABLE mm_purchase_order
    ADD COLUMN incoterms varchar,
    ADD COLUMN due_dt date,
    ADD COLUMN warehouse_id bigint,
    ADD COLUMN payment_terms varchar,
    ADD COLUMN buyer varchar,
    ADD COLUMN revision_no integer NOT NULL DEFAULT 0,
    ADD COLUMN approved_at timestamptz,
    ADD COLUMN approved_by varchar,
    ADD COLUMN closed_at timestamptz,
    ADD COLUMN closed_by varchar,
    ADD COLUMN cancelled_at timestamptz,
    ADD COLUMN cancelled_by varchar,
    ADD CONSTRAINT fk_mm_purchase_order_warehouse_id FOREIGN KEY (warehouse_id) REFERENCES bc_warehouse (id);
CREATE INDEX ix_mm_purchase_order_warehouse_id ON mm_purchase_order (warehouse_id);

ALTER TABLE mm_purchase_order_line
    ADD COLUMN gross_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN dc_rate numeric NOT NULL DEFAULT 0,
    ADD COLUMN dc_amt numeric NOT NULL DEFAULT 0,
    ADD COLUMN due_dt date,
    ADD COLUMN warehouse_id bigint,
    ADD COLUMN purchase_request_line_id bigint,
    ADD CONSTRAINT fk_mm_purchase_order_line_warehouse_id FOREIGN KEY (warehouse_id) REFERENCES bc_warehouse (id);
CREATE INDEX ix_mm_purchase_order_line_warehouse_id ON mm_purchase_order_line (warehouse_id);

-- Existing lines had no discount: gross = line amount
UPDATE mm_purchase_order_line SET gross_amt = po_amt WHERE po_amt IS NOT NULL;
