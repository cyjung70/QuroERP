-- The vendor API first stored the new vendor default terms as the text 'NET 30'. The TERMS code is NET_30 ('NET 30' is its
-- name), so those vendors get the code (MM-BR-851). Vendors with other values are left as they are.

UPDATE bc_vendor SET terms = 'NET_30' WHERE terms = 'NET 30';
