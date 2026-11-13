-- Sales invoice API (SD-UC-003): bill-first lots, unshipped cancellation links and the invoice list
--   sd_sales_invoice_line.lot_id: the lot a BILL_THEN_SHIP line moved to the consignment warehouse, so a cancellation moves
--     the same lot back (SD-BR-142, SD-BR-145, MM-BR-357).
--   sd_sales_invoice_line.cancelled_invoice_line_id: on a Credit Memo line made by an unshipped cancellation, the invoice line
--     it cancels (SD-BR-145, SD-BR-146).
--   ix_sd_sales_invoice_type_dn_dt: the invoice and Credit Memo lists read one type, newest first (SD-BR-120).
-- A database can already have these objects from an earlier script of the same content under another number, so each one is
-- added only when it is missing and the script gives the same schema either way.

ALTER TABLE sd_sales_invoice_line
    ADD COLUMN IF NOT EXISTS lot_id bigint,
    ADD COLUMN IF NOT EXISTS cancelled_invoice_line_id bigint;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'fk_sd_sales_invoice_line_lot_id' AND conrelid = 'sd_sales_invoice_line'::regclass) THEN
        ALTER TABLE sd_sales_invoice_line
            ADD CONSTRAINT fk_sd_sales_invoice_line_lot_id FOREIGN KEY (lot_id) REFERENCES mm_lot (id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'fk_sd_sales_invoice_line_cancelled_invoice_line_id'
                     AND conrelid = 'sd_sales_invoice_line'::regclass) THEN
        ALTER TABLE sd_sales_invoice_line
            ADD CONSTRAINT fk_sd_sales_invoice_line_cancelled_invoice_line_id
                FOREIGN KEY (cancelled_invoice_line_id) REFERENCES sd_sales_invoice_line (id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_sd_sales_invoice_line_lot_id ON sd_sales_invoice_line (lot_id);
CREATE INDEX IF NOT EXISTS ix_sd_sales_invoice_line_cancelled_invoice_line_id ON sd_sales_invoice_line (cancelled_invoice_line_id);

CREATE INDEX IF NOT EXISTS ix_sd_sales_invoice_type_dn_dt ON sd_sales_invoice (type, dn_dt);
