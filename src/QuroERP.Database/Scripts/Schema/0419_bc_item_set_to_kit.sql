-- Item type Set is split into Kit (KIT, sales BOM without stock of its own) and Set (SET, assembled stock) (SD-BR-511,
-- TASK-CORE-069). Every existing Set item expands into its components and has no stock, so it becomes a Kit. The KIT code
-- itself is added by the seed script 0508.
-- An applied script is not changed. Later changes go into a new script.

UPDATE bc_item SET type = 'KIT' WHERE type = 'SET';
