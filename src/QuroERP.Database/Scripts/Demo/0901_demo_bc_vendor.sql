-- Demo vendors for the vendor screen (--demo only). Fictional names, 555 phone numbers and example.com mail only.
-- Base currency and tax code NONE; one vendor is inactive so the active flag shows both values.
-- An applied script is not changed. More demo vendors go into a new script.

INSERT INTO bc_vendor (v_name, contact, phone1, email, terms, valid_flag, remarks, addr1, city, state, country, zip_code,
                       ship_addr1, ship_city, ship_state, ship_country, ship_zip_code, vendor_price, tax_code_id, created_by, updated_by)
SELECT v.v_name, v.contact, v.phone1, v.email, v.terms, v.valid_flag, v.remarks, v.addr1, v.city, v.state, 'USA', v.zip_code,
       v.addr1, v.city, v.state, 'USA', v.zip_code, v.v_name, t.id, 'system', 'system'
FROM (VALUES
    ('MAPLE LEAF SUPPLY', 'Dana Park', '555-0101', 'orders@maple.example.com', 'NET_30', 'Y', 'Main hardware supplier',
     '100 Sample Road', 'Springfield', 'CA', '90001'),
    ('NORTHWIND PARTS', 'Lee Grant', '555-0102', 'sales@northwind.example.com', 'NET_60', 'Y', NULL,
     '200 Demo Avenue', 'Riverside', 'NY', '10001'),
    ('OLD HARBOR TRADING', 'Sam Reed', '555-0103', NULL, 'NET_30', 'N', 'No longer used',
     '300 Test Street', 'Lakeside', 'CA', '90002')
) AS v (v_name, contact, phone1, email, terms, valid_flag, remarks, addr1, city, state, zip_code)
LEFT JOIN bc_tax_code t ON t.tax_code = 'NONE'
ON CONFLICT (v_name) DO NOTHING;
