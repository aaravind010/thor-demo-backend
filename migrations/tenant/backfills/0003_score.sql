-- requires: migration_probe.score
-- Fills the new score column for existing rows. Safe to re-run.
UPDATE migration_probe SET score = 0 WHERE score IS NULL;
