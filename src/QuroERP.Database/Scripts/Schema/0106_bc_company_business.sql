-- bc_company: business type and item for KR tax invoices (BC-BR-315, database-tables.md, TASK-BC-013)
--   Optional for US companies.

ALTER TABLE bc_company ADD COLUMN business_type varchar;
ALTER TABLE bc_company ADD COLUMN business_item varchar;
