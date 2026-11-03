-- Item barcodes never repeat across items (MM-BR-817); items without a barcode are left out.
CREATE UNIQUE INDEX uq_bc_item_upc_code ON bc_item (upc_code) WHERE upc_code IS NOT NULL;
