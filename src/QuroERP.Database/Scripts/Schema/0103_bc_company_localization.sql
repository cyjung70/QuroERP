-- bc_company: country, base currency, default language and tax calculation unit
--   (localization.md, BC company.md BC-BR-312~314, BC tax.md BC-BR-543, TASK-CORE-014)
--   Existing rows get US / USD / en, the legacy setup [추정]. The temporary defaults are dropped
--   so new rows must state the country values (from ICountryProfile).
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE bc_company ADD COLUMN country_code varchar NOT NULL DEFAULT 'US';
ALTER TABLE bc_company ADD COLUMN base_currency_code varchar NOT NULL DEFAULT 'USD';
ALTER TABLE bc_company ADD COLUMN default_lang_code varchar NOT NULL DEFAULT 'en';
ALTER TABLE bc_company ADD COLUMN tax_calc_mode varchar NOT NULL DEFAULT 'DOCUMENT';

ALTER TABLE bc_company ALTER COLUMN country_code DROP DEFAULT;
ALTER TABLE bc_company ALTER COLUMN base_currency_code DROP DEFAULT;
ALTER TABLE bc_company ALTER COLUMN default_lang_code DROP DEFAULT;

ALTER TABLE bc_company ADD CONSTRAINT ck_bc_company_country_code CHECK (country_code ~ '^[A-Z]{2}$');
ALTER TABLE bc_company ADD CONSTRAINT ck_bc_company_tax_calc_mode CHECK (tax_calc_mode IN ('DOCUMENT', 'LINE'));
ALTER TABLE bc_company ADD CONSTRAINT fk_bc_company_base_currency_code
    FOREIGN KEY (base_currency_code) REFERENCES bc_currency (currency_code);
ALTER TABLE bc_company ADD CONSTRAINT fk_bc_company_default_lang_code
    FOREIGN KEY (default_lang_code) REFERENCES sys_language (lang_code);
CREATE INDEX ix_bc_company_base_currency_code ON bc_company (base_currency_code);
CREATE INDEX ix_bc_company_default_lang_code ON bc_company (default_lang_code);
