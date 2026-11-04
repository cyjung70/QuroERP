-- Goods receipt keeps stock and cost only; the payable moves to the supplier invoice (TASK-MM-007)
-- Definition: database-tables.md (mm_goods_receipt, mm_goods_receipt_line, mm_inventory_setting), MM goods-receipt.md
--   MM-BR-104, MM-BR-108, MM-BR-112, MM-BR-163, MM-BR-353, MM-BR-102
-- Applied scripts are never edited. Add a new numbered script for changes.

-- mm_goods_receipt: no paid amount, balance, due date, terms or tax invoice flag; the supplier invoice has them
-- (MM-BR-112, MM-BR-163). Payment does not read the goods receipt any more
ALTER TABLE mm_goods_receipt
    DROP COLUMN cls_amt,
    DROP COLUMN bal_amt,
    DROP COLUMN due_dt,
    DROP COLUMN terms,
    DROP COLUMN IF EXISTS tax_invoice_received;

-- mm_goods_receipt_line: the lot of a lot-managed item (MM-BR-353, MM-BR-355) and the quantities that lock the receipt:
-- invoiced_qty = net supplier invoice quantity, returned_qty = vendor return quantity (MM-BR-104, MM-BR-753, MM-BR-786)
ALTER TABLE mm_goods_receipt_line
    ADD COLUMN lot_id bigint,
    ADD COLUMN invoiced_qty numeric NOT NULL DEFAULT 0,
    ADD COLUMN returned_qty numeric NOT NULL DEFAULT 0,
    ADD CONSTRAINT fk_mm_goods_receipt_line_lot_id FOREIGN KEY (lot_id) REFERENCES mm_lot (id);
CREATE INDEX ix_mm_goods_receipt_line_lot_id ON mm_goods_receipt_line (lot_id);

-- mm_purchase_order_line.receipt_qty: the received quantity the goods receipt keeps from now on (MM-BR-108)
UPDATE mm_purchase_order_line l
SET receipt_qty = coalesce((SELECT sum(g.gr_qty) FROM mm_goods_receipt_line g WHERE g.purchase_order_line_id = l.id), 0);

-- mm_inventory_setting.over_receipt_mode: receiving more than a PO line's remaining quantity is refused (DENY, default) or
-- saved after the user confirms a warning (WARN) (MM-BR-102)
ALTER TABLE mm_inventory_setting
    ADD COLUMN over_receipt_mode varchar NOT NULL DEFAULT 'DENY',
    ADD CONSTRAINT ck_mm_inventory_setting_over_receipt_mode CHECK (over_receipt_mode IN ('DENY', 'WARN'));
