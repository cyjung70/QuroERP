-- Set components may swap between rows in one save (TASK-BC-025)
-- Definition: database-tables.md sd_bom, SD set-item.md (SD-BR-504, SD-BR-506)
--   Before: UNIQUE (parent_item_id, child_item_id) is checked row by row, so changing row 1 A -> B and row 2 B -> A in one save
--           fails at the first update although the saved set is valid.
--   After:  the same key checked at commit. The Domain checks duplicates before anything is written, and saves of one set
--           run one at a time (the set item row is locked).
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE sd_bom
    DROP CONSTRAINT uq_sd_bom_parent_item_id_child_item_id,
    ADD CONSTRAINT uq_sd_bom_parent_item_id_child_item_id UNIQUE (parent_item_id, child_item_id) DEFERRABLE INITIALLY DEFERRED;
