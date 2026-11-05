-- Demo customers for the sales invoice screen (--demo only). Fictional names, 555 phone numbers and example.com mail only.
-- One customer bills after shipping (shipment lines are imported into an invoice, SD-BR-115) and one bills first (bill-first
-- order lines are imported, then shipped from the consignment warehouse, SD-BR-141). Both have the demo rep DEMO_A (0905),
-- because an invoice needs a sales rep. No demo orders, shipments or invoices: they are made on the screens.
-- An applied script is not changed. More demo data goes into a new script.

INSERT INTO bc_customer (c_name, ship_name, contact, phone1, email, rep, price_level, terms, valid_flag, remarks, addr1, city, state,
                         country, zip_code, ship_addr1, ship_city, ship_state, ship_country, ship_zip_code, credit_limit, start_dt,
                         tax_code_id, billing_flow, created_by, updated_by)
SELECT v.c_name, v.c_name, v.contact, v.phone1, v.email, 'DEMO_A', v.c_name, 'NET_30', 'Y', v.remarks, v.addr1, v.city, v.state,
       'USA', v.zip_code, v.addr1, v.city, v.state, 'USA', v.zip_code, 0, DATE '2026-01-05', t.id, v.billing_flow, 'system', 'system'
FROM (VALUES
    ('MAPLE HILL SUPPLY', 'Sam Reed', '555-0204', 'ap@maplehill.example.com', 'Ships first, invoiced later',
     '40 Example Road', 'Springfield', 'CA', '90030', 'SHIP_THEN_BILL'),
    ('CEDAR POINT TRADING', 'Taylor Kim', '555-0205', 'ap@cedarpoint.example.com', 'Invoiced before shipping',
     '50 Sample Street', 'Riverside', 'NY', '10020', 'BILL_THEN_SHIP')
) AS v (c_name, contact, phone1, email, remarks, addr1, city, state, zip_code, billing_flow)
LEFT JOIN bc_tax_code t ON t.tax_code = 'NONE'
ON CONFLICT (c_name) DO NOTHING;
