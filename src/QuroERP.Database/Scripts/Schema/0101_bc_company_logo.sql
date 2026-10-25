-- bc_company: company logo for printed documents (ADR-0024, print-documents.md)
--   logo_image:        image bytes (PNG or JPEG), uploaded on the company screen
--   logo_content_type: MIME type of logo_image
--   Both are NULL when no logo is set; printed documents then show the company name only
-- Applied scripts are never edited. Add a new numbered script for changes.

ALTER TABLE bc_company ADD COLUMN logo_image bytea;
ALTER TABLE bc_company ADD COLUMN logo_content_type varchar;
ALTER TABLE bc_company ADD CONSTRAINT ck_bc_company_logo_content_type
    CHECK (logo_content_type IN ('image/png', 'image/jpeg'));
ALTER TABLE bc_company ADD CONSTRAINT ck_bc_company_logo_pair
    CHECK ((logo_image IS NULL) = (logo_content_type IS NULL));
