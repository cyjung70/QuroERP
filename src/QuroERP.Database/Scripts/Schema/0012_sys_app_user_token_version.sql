-- Token version of a user (BC-BR-058, BC-BR-130, security.md, TASK-BC-003)
--   Goes up on password change, reset, not in use and forced revoke. Token refresh rejects a token with an older version.

ALTER TABLE sys_app_user ADD COLUMN token_version integer NOT NULL DEFAULT 0;
