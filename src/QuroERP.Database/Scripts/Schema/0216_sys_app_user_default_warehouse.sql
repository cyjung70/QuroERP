-- sys_app_user.default_warehouse_id: the user's default warehouse, NULL means the company default (login.md "me").
-- Kept apart from 0008 because bc_warehouse is created in 0213.
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE sys_app_user ADD COLUMN default_warehouse_id bigint;
ALTER TABLE sys_app_user ADD CONSTRAINT fk_sys_app_user_default_warehouse_id
    FOREIGN KEY (default_warehouse_id) REFERENCES bc_warehouse (id);
CREATE INDEX ix_sys_app_user_default_warehouse_id ON sys_app_user (default_warehouse_id);
