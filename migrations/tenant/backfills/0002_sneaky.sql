-- requires: migration_probe.new_note
-- TEST ONLY — DDL hidden in a DO block. The CI text check can't see it; the schema check inside the
-- migrate transaction must fail and roll the whole tenant back (including this UPDATE).
UPDATE migration_probe SET new_note = upper(new_note) WHERE new_note IS NOT NULL;
DO $$ BEGIN EXECUTE 'ALTER TABLE tenant.migration_probe ADD COLUMN sneaky integer'; END $$;
