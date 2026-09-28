#!/bin/sh
# Tests for classify.sh — one case per rule. Run: sh migrations/tenant/classify_test.sh
# (CI runs it in tenant-migrations.yml's generate job.)
#
# Each case: expand SQL, full SQL, live NOT NULL columns, the expected findings as
# "<class> <rule> <table> <sql>" lines (rule is "-" for expand), and optionally the known drops.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
failed=0

check() { # name expand_sql full_sql notnull expected [known_drops]
  printf '%s\n' "$2" > "$tmp/expand.sql"
  printf '%s\n' "$3" > "$tmp/full.sql"
  printf '%s\n' "$4" > "$tmp/notnull.txt"
  printf '%s\n' "${6:-}" > "$tmp/known.txt"
  actual=$(sh "$here/classify.sh" "$tmp/expand.sql" "$tmp/full.sql" "$tmp/notnull.txt" "$tmp/known.txt" | jq -r '
    (.expand[]   | "expand - \(.table) \(.sql)"),
    (.deferred[] | "deferred \(.rule) \(.table) \(.sql)"),
    (.blocking[] | "blocking \(.rule) \(.table) \(.sql)")')
  if [ "$actual" = "$5" ]; then
    echo "ok   $1"
  else
    echo "FAIL $1"
    printf '  expected:\n%s\n  actual:\n%s\n' "$5" "$actual"
    failed=1
  fi
}

# --- expand: applied ---

check add-table \
'-- Create "audit_note" table
CREATE TABLE "tenant"."audit_note" ("id" uuid NOT NULL, "scan_id" uuid NOT NULL, "body" text NULL, PRIMARY KEY ("id"));
CREATE INDEX "ix_audit_note_scan_id" ON "tenant"."audit_note" ("scan_id");
ALTER TABLE "tenant"."audit_note" ADD CONSTRAINT "fk_audit_note_scan" FOREIGN KEY ("scan_id") REFERENCES "tenant"."scan" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT;' \
'CREATE TABLE "tenant"."audit_note" ("id" uuid NOT NULL, "scan_id" uuid NOT NULL, "body" text NULL, PRIMARY KEY ("id"));' \
'' \
'expand - audit_note CREATE TABLE "tenant"."audit_note" ("id" uuid NOT NULL, "scan_id" uuid NOT NULL, "body" text NULL, PRIMARY KEY ("id"))
expand - audit_note CREATE INDEX "ix_audit_note_scan_id" ON "tenant"."audit_note" ("scan_id")
expand - audit_note ALTER TABLE "tenant"."audit_note" ADD CONSTRAINT "fk_audit_note_scan" FOREIGN KEY ("scan_id") REFERENCES "tenant"."scan" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT'

# Commas inside numeric(10,2) must not split the clause.
check add-nullable-columns \
'ALTER TABLE "tenant"."scan" ADD COLUMN "score" numeric(10,2) NULL, ADD COLUMN "notes" text NULL;' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "score" numeric(10,2) NULL, ADD COLUMN "notes" text NULL;' \
'' \
'expand - scan ALTER TABLE "tenant"."scan" ADD COLUMN "score" numeric(10,2) NULL
expand - scan ALTER TABLE "tenant"."scan" ADD COLUMN "notes" text NULL'

check not-null-with-default \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL DEFAULT 0;' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL DEFAULT 0;' \
'' \
'expand - scan ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL DEFAULT 0'

# The model made a column optional: DROP NOT NULL is backward compatible, so it runs in expand.
check relax-not-null \
'' \
'ALTER TABLE "tenant"."scan" ALTER COLUMN "status" DROP NOT NULL;' \
'' \
'expand - scan ALTER TABLE "tenant"."scan" ALTER COLUMN "status" DROP NOT NULL'

# A required property was removed: relax the column now, drop it in the contract phase.
check drop-not-null-column \
'' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'scan.legacy' \
'expand - scan ALTER TABLE "tenant"."scan" ALTER COLUMN "legacy" DROP NOT NULL
deferred drop-column scan ALTER TABLE "tenant"."scan" DROP COLUMN "legacy"'

check relax-with-add \
'ALTER TABLE "tenant"."scan" ADD COLUMN "notes" text NULL;' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "notes" text NULL, ALTER COLUMN "status" DROP NOT NULL;' \
'' \
'expand - scan ALTER TABLE "tenant"."scan" ADD COLUMN "notes" text NULL
expand - scan ALTER TABLE "tenant"."scan" ALTER COLUMN "status" DROP NOT NULL'

# --- blocking: plan fails ---

check not-null-without-default \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL;' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL;' \
'' \
'blocking not-null-without-default scan ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL'

check volatile-default \
'ALTER TABLE "tenant"."scan" ADD COLUMN "ref" uuid NOT NULL DEFAULT gen_random_uuid();' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "ref" uuid NOT NULL DEFAULT gen_random_uuid();' \
'' \
'blocking volatile-default scan ALTER TABLE "tenant"."scan" ADD COLUMN "ref" uuid NOT NULL DEFAULT gen_random_uuid()'

check suspected-rename \
'ALTER TABLE "tenant"."scan" ADD COLUMN "display_name" text NULL;' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "name", ADD COLUMN "display_name" text NULL;' \
'' \
'expand - scan ALTER TABLE "tenant"."scan" ADD COLUMN "display_name" text NULL
blocking suspected-rename scan ALTER TABLE "tenant"."scan" DROP COLUMN "name"'

# The rename check wins over relaxing a removed NOT NULL column.
check suspected-rename-not-null \
'ALTER TABLE "tenant"."scan" ADD COLUMN "display_name" text NULL;' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "name", ADD COLUMN "display_name" text NULL;' \
'scan.name' \
'expand - scan ALTER TABLE "tenant"."scan" ADD COLUMN "display_name" text NULL
blocking suspected-rename scan ALTER TABLE "tenant"."scan" DROP COLUMN "name"'

# old_name was removed in an earlier model version and still waits for the contract phase; adding
# a column to the same table now is not a rename.
check known-drop-then-add \
'ALTER TABLE "tenant"."scan" ADD COLUMN "score" integer NULL;' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "old_name", ADD COLUMN "score" integer NULL;' \
'' \
'expand - scan ALTER TABLE "tenant"."scan" ADD COLUMN "score" integer NULL
deferred drop-column scan ALTER TABLE "tenant"."scan" DROP COLUMN "old_name"' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "old_name"'

# Widening too: the new code would write values the old column can't hold.
check type-change-widen \
'' \
'ALTER TABLE "tenant"."scan" ALTER COLUMN "title" TYPE character varying(128);' \
'' \
'blocking type-change scan ALTER TABLE "tenant"."scan" ALTER COLUMN "title" TYPE character varying(128)'

# Safety net: something the skip policy should have removed reached the plan.
check not-additive-leak \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'' \
'deferred drop-column scan ALTER TABLE "tenant"."scan" DROP COLUMN "legacy"
blocking not-additive scan ALTER TABLE "tenant"."scan" DROP COLUMN "legacy"'

# --- deferred: reported as pending contract, not applied ---

check drop-nullable-column \
'' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'' \
'deferred drop-column scan ALTER TABLE "tenant"."scan" DROP COLUMN "legacy"'

# EF's own history table is never reported; the type change in the same statement blocks.
check contract-deferred \
'' \
'-- Drop "old_report" table
DROP TABLE "tenant"."old_report";
ALTER TABLE "tenant"."scan" ALTER COLUMN "status" TYPE character varying(64), ALTER COLUMN "status" SET NOT NULL;
DROP TABLE "tenant"."__EFMigrationsHistory";' \
'' \
'deferred contract old_report DROP TABLE "tenant"."old_report"
deferred contract scan ALTER TABLE "tenant"."scan" ALTER COLUMN "status" SET NOT NULL
blocking type-change scan ALTER TABLE "tenant"."scan" ALTER COLUMN "status" TYPE character varying(64)'

exit "$failed"
