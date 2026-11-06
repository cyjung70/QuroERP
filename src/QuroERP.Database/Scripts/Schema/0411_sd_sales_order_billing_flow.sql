-- Sales order billing flow (SD-BR-035, BC-BR-316): copied from the customer when the order is made and changeable on the order
-- until a shipment or invoice exists. Existing orders get their customer's billing flow; orders without a customer get the
-- column default.

ALTER TABLE sd_sales_order ADD COLUMN billing_flow varchar NOT NULL DEFAULT 'SHIP_AND_BILL',
    ADD CONSTRAINT ck_sd_sales_order_billing_flow CHECK (billing_flow IN ('SHIP_AND_BILL', 'SHIP_THEN_BILL', 'BILL_THEN_SHIP'));

UPDATE sd_sales_order o
SET billing_flow = c.billing_flow
FROM bc_customer c
WHERE c.id = o.customer_id AND o.billing_flow <> c.billing_flow;
