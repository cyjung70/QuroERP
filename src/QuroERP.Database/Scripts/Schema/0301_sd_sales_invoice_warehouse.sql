-- warehouse_id on sd_sales_invoice and sd_sales_invoice_line (ADR-0027, MM-BR-703)
--   The header warehouse is the line default; lines carry the movement warehouse
--   Existing rows go to the default warehouse (0213_mm_warehouse_stock.sql)
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE sd_sales_invoice ADD COLUMN warehouse_id bigint;
ALTER TABLE sd_sales_invoice_line ADD COLUMN warehouse_id bigint;

UPDATE sd_sales_invoice SET warehouse_id = (SELECT id FROM bc_warehouse WHERE is_default);
UPDATE sd_sales_invoice_line SET warehouse_id = (SELECT id FROM bc_warehouse WHERE is_default);

ALTER TABLE sd_sales_invoice ALTER COLUMN warehouse_id SET NOT NULL,
    ADD CONSTRAINT fk_sd_sales_invoice_warehouse_id FOREIGN KEY (warehouse_id) REFERENCES bc_warehouse (id);
ALTER TABLE sd_sales_invoice_line ALTER COLUMN warehouse_id SET NOT NULL,
    ADD CONSTRAINT fk_sd_sales_invoice_line_warehouse_id FOREIGN KEY (warehouse_id) REFERENCES bc_warehouse (id);

CREATE INDEX ix_sd_sales_invoice_warehouse_id ON sd_sales_invoice (warehouse_id);
CREATE INDEX ix_sd_sales_invoice_line_warehouse_id ON sd_sales_invoice_line (warehouse_id);
