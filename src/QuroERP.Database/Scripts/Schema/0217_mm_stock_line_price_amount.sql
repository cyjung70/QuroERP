-- Unit cost and amount on stock transfer and stock count lines (database-tables.md)
--   mm_stock_transfer_line: prc = moving average cost at transfer, amt = round(qty × prc, base currency decimals) (MM-BR-706)
--   mm_stock_count_line: prc = moving average cost at the count date, used for dif_amt and new_amt (MM-BR-303)
-- No screen writes these tables yet, so existing transfer lines (none expected) get 0 before NOT NULL.

ALTER TABLE mm_stock_transfer_line ADD COLUMN prc numeric;
ALTER TABLE mm_stock_transfer_line ADD COLUMN amt numeric;
UPDATE mm_stock_transfer_line SET prc = 0, amt = 0;
ALTER TABLE mm_stock_transfer_line
    ALTER COLUMN prc SET NOT NULL,
    ALTER COLUMN amt SET NOT NULL;

ALTER TABLE mm_stock_count_line ADD COLUMN prc numeric;
