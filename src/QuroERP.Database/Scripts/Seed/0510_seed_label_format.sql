-- Default label formats, one per label type (TASK-CORE-070, MM-BR-917). Zebra ZPL at 203 dpi; the fields print in this order.
-- An existing format is kept: it may already be changed on the label format screen.
INSERT INTO bc_label_format (label_type, output_kind, width_mm, height_mm, dpi, fields, created_by, updated_by)
VALUES
    ('PALLET', 'ZPL', 100, 150, 203, '["palletNo", "itemCd", "itemName", "lotNo", "qty", "expiryDt"]', 'system', 'system'),
    ('ITEM', 'ZPL', 50, 25, 203, '["itemCd", "itemName"]', 'system', 'system'),
    ('LOT', 'ZPL', 50, 25, 203, '["itemCd", "lotNo", "expiryDt"]', 'system', 'system'),
    ('LOCATION', 'ZPL', 100, 50, 203, '["warehouseCode", "locationCode"]', 'system', 'system')
ON CONFLICT (label_type) DO NOTHING;
