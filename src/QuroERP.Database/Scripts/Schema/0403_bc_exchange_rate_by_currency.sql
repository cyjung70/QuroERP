-- Exchange rates by date and currency (TASK-BC-047)
-- Definition: database-tables.md bc_exchange_rate, SD master-data.md (SD-BR-906, SD-BR-907, SD-BR-909)
--   Before: one row per date with two legacy columns, cur_rp (rupiah per 1 USD) and cur_won (won per 1 USD), unique cur_dt.
--   After:  one row per (rate_dt, currency_code); rate is the base currency amount per 1 unit of the currency.
--   Rows:   each legacy row becomes an IDR and a KRW row with the inverse 1 / value rounded to 10 decimals (SD-BR-909).
--           A missing or non-positive legacy value has no rate and makes no row.
--   The unique key is checked at commit, so one save may swap the dates or currencies of two rows. Saves run one at a time
--   (the repository takes a lock), and the Domain checks duplicates before anything is written (SD-BR-906).
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE bc_exchange_rate
    DROP CONSTRAINT uq_bc_exchange_rate_cur_dt,
    ALTER COLUMN cur_dt DROP NOT NULL,
    ADD COLUMN rate_dt date,
    ADD COLUMN currency_code varchar,
    ADD COLUMN rate numeric;

INSERT INTO bc_exchange_rate (rate_dt, currency_code, rate, created_at, created_by, updated_at, updated_by)
SELECT r.cur_dt, v.currency_code, round(1 / v.per_usd, 10), r.created_at, r.created_by, r.updated_at, r.updated_by
FROM bc_exchange_rate r
CROSS JOIN LATERAL (VALUES ('IDR', r.cur_rp), ('KRW', r.cur_won)) AS v (currency_code, per_usd)
WHERE r.rate_dt IS NULL AND v.per_usd > 0;

DELETE FROM bc_exchange_rate WHERE rate_dt IS NULL;

ALTER TABLE bc_exchange_rate
    DROP COLUMN cur_dt,
    DROP COLUMN cur_rp,
    DROP COLUMN cur_won,
    ALTER COLUMN rate_dt SET NOT NULL,
    ALTER COLUMN currency_code SET NOT NULL,
    ALTER COLUMN rate SET NOT NULL,
    ADD CONSTRAINT uq_bc_exchange_rate_rate_dt_currency_code UNIQUE (rate_dt, currency_code) DEFERRABLE INITIALLY DEFERRED;
