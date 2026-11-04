-- Customer billing flow (SD-BR-335, BC-BR-316): the default of the customer's new sales orders. Existing customers get the
-- company billing setting as a billing flow (WITH_SHIPMENT = SHIP_AND_BILL, SEPARATE = SHIP_THEN_BILL).

ALTER TABLE bc_customer ADD COLUMN billing_flow varchar NOT NULL DEFAULT 'SHIP_AND_BILL',
    ADD CONSTRAINT ck_bc_customer_billing_flow CHECK (billing_flow IN ('SHIP_AND_BILL', 'SHIP_THEN_BILL', 'BILL_THEN_SHIP'));

UPDATE bc_customer
SET billing_flow = 'SHIP_THEN_BILL'
WHERE (SELECT billing_mode FROM bc_company ORDER BY id LIMIT 1) = 'SEPARATE';
