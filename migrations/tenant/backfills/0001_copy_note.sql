-- requires: migration_probe.old_note, migration_probe.new_note
-- Rename recipe, migrate step: copy the old note into the new column. Safe to re-run.
UPDATE migration_probe SET new_note = old_note WHERE new_note IS NULL AND old_note IS NOT NULL;
