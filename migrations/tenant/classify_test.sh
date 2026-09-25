#!/bin/sh
# Tests for classify.sh — one case per rule. Run: sh migrations/tenant/classify_test.sh
# (CI runs it in tenant-migrations.yml's generate job.)
#
# Each case: expand SQL, full SQL, live NOT NULL columns, then the expected findings as
# "<class> <rule> <table> <sql>" lines (rule is "-" for expand).
set -eu

here=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
failed=0

check() { # name expand_sql full_sql notnull expected
  printf '%s\n' "$2" > "$tmp/expand.sql"
  printf '%s\n' "$3" > "$tmp/full.sql"
  printf '%s\n' "$4" > "$tmp/notnull.txt"
  actual=$(sh "$here/classify.sh" "$tmp/expand.sql" "$tmp/full.sql" "$tmp/notnull.txt" | jq -r '
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
expand - audit_note ADD CONSTRAINT "fk_audit_note_scan" FOREIGN KEY ("scan_id") REFERENCES "tenant"."scan" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT'

# Commas inside numeric(10,2) must not split the clause.
check add-nullable-columns \
'ALTER TABLE "tenant"."scan" ADD COLUMN "score" numeric(10,2) NULL, ADD COLUMN "notes" text NULL;' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "score" numeric(10,2) NULL, ADD COLUMN "notes" text NULL;' \
'' \
'expand - scan ADD COLUMN "score" numeric(10,2) NULL
expand - scan ADD COLUMN "notes" text NULL'

check not-null-with-default \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL DEFAULT 0;' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL DEFAULT 0;' \
'' \
'expand - scan ADD COLUMN "priority" integer NOT NULL DEFAULT 0'

# --- blocking: plan fails ---

check not-null-without-default \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL;' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "priority" integer NOT NULL;' \
'' \
'blocking not-null-without-default scan ADD COLUMN "priority" integer NOT NULL'

check volatile-default \
'ALTER TABLE "tenant"."scan" ADD COLUMN "ref" uuid NOT NULL DEFAULT gen_random_uuid();' \
'ALTER TABLE "tenant"."scan" ADD COLUMN "ref" uuid NOT NULL DEFAULT gen_random_uuid();' \
'' \
'blocking volatile-default scan ADD COLUMN "ref" uuid NOT NULL DEFAULT gen_random_uuid()'

check suspected-rename \
'ALTER TABLE "tenant"."scan" ADD COLUMN "display_name" text NULL;' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "name", ADD COLUMN "display_name" text NULL;' \
'' \
'expand - scan ADD COLUMN "display_name" text NULL
blocking suspected-rename scan DROP COLUMN "name"'

check drop-not-null-column \
'' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'scan.legacy' \
'blocking drop-not-null-column scan DROP COLUMN "legacy"'

check relax-not-null \
'' \
'ALTER TABLE "tenant"."scan" ALTER COLUMN "status" DROP NOT NULL;' \
'' \
'blocking relax-not-null scan ALTER COLUMN "status" DROP NOT NULL'

# Safety net: something the skip policy should have removed reached the plan.
check not-additive-leak \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'' \
'deferred drop-column scan DROP COLUMN "legacy"
blocking not-additive scan DROP COLUMN "legacy"'

# --- deferred: reported as pending contract, not applied ---

check drop-nullable-column \
'' \
'ALTER TABLE "tenant"."scan" DROP COLUMN "legacy";' \
'' \
'deferred drop-column scan DROP COLUMN "legacy"'

# EF's own history table is never reported.
check contract-deferred \
'' \
'-- Drop "old_report" table
DROP TABLE "tenant"."old_report";
ALTER TABLE "tenant"."scan" ALTER COLUMN "status" TYPE character varying(64), ALTER COLUMN "status" SET NOT NULL;
DROP TABLE "tenant"."__EFMigrationsHistory";' \
'' \
'deferred contract old_report DROP TABLE "tenant"."old_report"
deferred contract scan ALTER COLUMN "status" TYPE character varying(64)
deferred contract scan ALTER COLUMN "status" SET NOT NULL'

exit "$failed"
