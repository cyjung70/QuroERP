-- Indexes for the foreign keys 0421 left without one (TASK-SD-072, quro-dapper checklist)
-- Every step checks what is already there, so the script gives the same schema when it runs again.
-- Applied scripts are never edited. Add a new numbered script for changes.

CREATE INDEX IF NOT EXISTS ix_mm_assembly_warehouse_id ON mm_assembly (warehouse_id);
CREATE INDEX IF NOT EXISTS ix_mm_assembly_location_id ON mm_assembly (location_id);
CREATE INDEX IF NOT EXISTS ix_mm_assembly_set_lot_id ON mm_assembly (set_lot_id);
CREATE INDEX IF NOT EXISTS ix_mm_assembly_line_warehouse_id ON mm_assembly_line (warehouse_id);
CREATE INDEX IF NOT EXISTS ix_mm_assembly_line_location_id ON mm_assembly_line (location_id);
