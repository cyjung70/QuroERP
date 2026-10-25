-- Drop mm_purchase_simulation and mm_purchase_simulation_line (purchase-simulation.md):
--   Simulation results are not stored. The screen calculates recommended quantities and creates purchase orders
--   from the selected lines, so these tables are not used. They are not listed in database-tables.md.
-- Applied scripts are never edited. Add a new numbered script for changes.

DROP TABLE mm_purchase_simulation_line;
DROP TABLE mm_purchase_simulation;
