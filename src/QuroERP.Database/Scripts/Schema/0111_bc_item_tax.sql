-- Item tax kind (MM-BR-831): US taxable flag and KR VAT kind, used as line tax code defaults (BC-BR-542).
ALTER TABLE bc_item ADD COLUMN taxable boolean NOT NULL DEFAULT true,
    ADD COLUMN vat_kind varchar NOT NULL DEFAULT 'TAXABLE',
    ADD CONSTRAINT ck_bc_item_vat_kind CHECK (vat_kind IN ('TAXABLE', 'EXEMPT'));
