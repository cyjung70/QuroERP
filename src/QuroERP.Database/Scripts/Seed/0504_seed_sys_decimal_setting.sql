-- sys_decimal_setting defaults by company country (BC-BR-422, ICountryProfile.DefaultDecimalSettings)
--   US: MM unit price 4, qty 2 / SD unit price 2, qty 0
--   KR: MM unit price 2, qty 2 / SD unit price 0, qty 0
--   Without a company the US defaults apply [추정]. All modules start with HALF_UP (BC-BR-424).
-- Applied scripts are never edited. Add a new numbered script for changes.

INSERT INTO sys_decimal_setting (module, unit_price_decimals, qty_decimals, rounding_mode, created_by, updated_by)
SELECT v.module, v.unit_price_decimals, v.qty_decimals, 'HALF_UP', 'system', 'system'
FROM (VALUES
    ('US', 'MM', 4, 2),
    ('US', 'SD', 2, 0),
    ('KR', 'MM', 2, 2),
    ('KR', 'SD', 0, 0)
) AS v (country_code, module, unit_price_decimals, qty_decimals)
WHERE v.country_code = COALESCE((SELECT country_code FROM bc_company ORDER BY id LIMIT 1), 'US');
