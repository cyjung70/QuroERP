-- Set composition constraints and the set source of goods movements (TASK-CORE-011)
-- Definition: docs/architecture/database-tables.md (sd_bom, mm_goods_movement), inventory.md "검사 순서"
-- Applied scripts are never edited. Add a new numbered script for changes.

-- Stop before adding constraints when existing rows break them, listing every such row
DO $$
DECLARE
    bad text;
BEGIN
    SELECT string_agg(format('id=%s set=%s component=%s qty=%s', b.id, b.parent_item_id, b.child_item_id, b.child_qty), '; ' ORDER BY b.id)
      INTO bad
      FROM sd_bom b
     WHERE b.parent_item_id IS NULL
        OR b.child_item_id IS NULL
        OR b.child_qty <= 0
        OR b.parent_item_id = b.child_item_id
        OR EXISTS (SELECT 1 FROM sd_bom d
                    WHERE d.parent_item_id = b.parent_item_id AND d.child_item_id = b.child_item_id AND d.id <> b.id);
    IF bad IS NOT NULL THEN
        RAISE EXCEPTION 'sd_bom rows break SD-BR-503/504 or have no set or component, fix them first: %', bad;
    END IF;
END $$;

-- sd_bom: required set and component, one row per component (SD-BR-504), qty above zero (SD-BR-503), no self
ALTER TABLE sd_bom
    ALTER COLUMN parent_item_id SET NOT NULL,
    ALTER COLUMN child_item_id SET NOT NULL,
    ADD CONSTRAINT uq_sd_bom_parent_item_id_child_item_id UNIQUE (parent_item_id, child_item_id),
    ADD CONSTRAINT ck_sd_bom_child_qty CHECK (child_qty > 0),
    ADD CONSTRAINT ck_sd_bom_not_self CHECK (parent_item_id <> child_item_id);

-- mm_goods_movement.set_item_id: the set line a component movement was expanded from (MM-BR-007)
ALTER TABLE mm_goods_movement
    ADD COLUMN set_item_id bigint,
    ADD CONSTRAINT fk_mm_goods_movement_set_item_id FOREIGN KEY (set_item_id) REFERENCES bc_item (id);
CREATE INDEX ix_mm_goods_movement_set_item_id ON mm_goods_movement (set_item_id);
