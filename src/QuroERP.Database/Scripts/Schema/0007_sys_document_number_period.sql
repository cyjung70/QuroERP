-- sys_document_number: one counter per (trans_type, period) (SD-BR-006, database-tables.md)
--   Before: trans_type held prefix + month (e.g. GM202610), trans_no nullable, UNIQUE (trans_type)
--   After:  trans_type is the prefix, period is the document date's yymm ('' for continuous numbers such as DN),
--           trans_no NOT NULL, UNIQUE (trans_type, period)
--   Existing rows keep their trans_type with period ''. They are the old GM counters, which MM-BR-607 replaced
--   with ID, so no new number is issued from them. Rows without a number are dropped
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE sys_document_number ADD COLUMN period varchar NOT NULL DEFAULT '';
ALTER TABLE sys_document_number ALTER COLUMN period DROP DEFAULT;

DELETE FROM sys_document_number WHERE trans_no IS NULL;
ALTER TABLE sys_document_number ALTER COLUMN trans_no SET NOT NULL;

ALTER TABLE sys_document_number DROP CONSTRAINT uq_sys_document_number_trans_type;
ALTER TABLE sys_document_number ADD CONSTRAINT uq_sys_document_number_trans_type_period UNIQUE (trans_type, period);
