-- Price levels are stored trimmed and in upper case, so the sales price table finds a customer's price level
-- (SD-BR-304, SD-BR-409, TASK-BC-028). Existing customer and sales price rows are aligned here. Where two price rows of one
-- item become the same level, the row that was already trimmed and upper case stays; when none was, the most recently
-- updated row stays.
-- An applied script is not changed. Later changes go into a new script.

DELETE FROM bc_sales_price p
USING (
    SELECT id,
           row_number() OVER (
               PARTITION BY upper(btrim(price_level)), item_id
               ORDER BY (price_level = upper(btrim(price_level))) DESC, updated_at DESC, id DESC) AS keep_order
    FROM bc_sales_price
) r
WHERE p.id = r.id
  AND r.keep_order > 1;

UPDATE bc_sales_price SET price_level = upper(btrim(price_level)) WHERE price_level <> upper(btrim(price_level));

UPDATE bc_customer SET price_level = upper(btrim(price_level)) WHERE price_level <> upper(btrim(price_level));
