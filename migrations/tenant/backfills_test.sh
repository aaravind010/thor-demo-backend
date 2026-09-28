#!/bin/sh
# Tests for backfills.sh — one case per rule. Run: sh migrations/tenant/backfills_test.sh
# (CI runs it in tenant-migrations.yml's generate job.)
#
# Each case: scripts written with script(), then the tenant's record ("id sha256|same" lines, where
# "same" means the script's current hash), its columns, and the expected findings as
# "pending <id>", "done <id>", "skipped <id> <cols>", "blocking <rule> <id> <detail>" lines.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
failed=0

scripts() { rm -rf "$tmp/b"; mkdir -p "$tmp/b"; } # start a case with an empty backfills folder
script() { printf '%s\n' "$2" > "$tmp/b/$1.sql"; }  # id content

check() { # name record columns expected
  : > "$tmp/record.tsv"
  printf '%s\n' "$2" | while read -r id sha; do
    [ -n "$id" ] || continue
    [ "$sha" = same ] && sha=$(sha256sum "$tmp/b/$id.sql" | cut -d' ' -f1)
    printf '%s\t%s\n' "$id" "$sha" >> "$tmp/record.tsv"
  done
  printf '%s\n' "$3" > "$tmp/columns.txt"
  actual=$(sh "$here/backfills.sh" "$tmp/b" "$tmp/record.tsv" "$tmp/columns.txt" | jq -r '
    (.pending[]  | "pending \(.id)"),
    (.done[]     | "done \(.)"),
    (.skipped[]  | "skipped \(.id) \(.missing | join(","))"),
    (.blocking[] | "blocking \(.rule) \(.id) \(.detail)")')
  if [ "$actual" = "$4" ]; then
    echo "ok   $1"
  else
    echo "FAIL $1"
    printf '  expected:\n%s\n  actual:\n%s\n' "$4" "$actual"
    failed=1
  fi
}

# --- which scripts run ---

scripts
check no-scripts '' 'scan.id' ''

scripts
script 0002_second '-- requires: scan.id
UPDATE scan SET score = 0 WHERE score IS NULL;'
script 0001_first '-- requires: scan.id
UPDATE scan SET status = '\''new'\'' WHERE status IS NULL;'
check pending-in-order '' 'scan.id' \
'pending 0001_first
pending 0002_second'

# A record row for a script that was deleted later is ignored.
check done-and-pending '0001_first same
0000_deleted abc' 'scan.id' \
'pending 0002_second
done 0001_first'

check edited-after-run '0001_first 1111' 'scan.id' \
'pending 0002_second
blocking backfill-changed 0001_first edited after it ran — add a new script instead'

# The tenant was provisioned after old_name was removed: nothing to copy.
scripts
script 0001_copy_name '-- requires: scan.old_name, scan.new_name
UPDATE scan SET new_name = old_name WHERE new_name IS NULL;'
check column-missing '' 'scan.id
scan.new_name' \
'skipped 0001_copy_name scan.old_name'

scripts
script 0001_no_header 'UPDATE scan SET score = 0 WHERE score IS NULL;'
check no-requires '' 'scan.id' \
'blocking backfill-no-requires 0001_no_header add a line: -- requires: <table.column>, ...'

scripts
script fix_scores '-- requires: scan.id
UPDATE scan SET score = 0;'
check bad-name '' 'scan.id' \
'blocking backfill-bad-name fix_scores name it NNNN_<name>.sql'

# The migrate plan appends the record row after the script; a missing ";" would merge the two.
scripts
script 0001_open_end '-- requires: scan.id
UPDATE scan SET score = 0 WHERE score IS NULL'
check unterminated '' 'scan.id' \
'blocking backfill-unterminated 0001_open_end line 2: end the last statement with ;'

# --- only data statements (DDL guard, layer 1) ---

scripts
script 0001_alter '-- requires: scan.id
UPDATE scan SET score = 0 WHERE score IS NULL;
ALTER TABLE scan ADD COLUMN extra int;'
check ddl-alter '' 'scan.id' \
'blocking backfill-ddl 0001_alter line 3: ALTER'

# DROP inside a comment or a string is not a statement; neither is a ";" inside a string.
scripts
script 0001_text '-- requires: scan.id
-- nothing here should drop anything
UPDATE scan SET note = '\''please drop this; now'\'' WHERE note IS NULL;
/* DROP TABLE scan; */
UPDATE "scan" SET note = '\''it'\'''\''s fine'\'' WHERE id IS NULL;'
check comments-and-strings '' 'scan.id' \
'pending 0001_text'

scripts
script 0001_batch '-- requires: scan.id
CREATE TEMP TABLE batch ON COMMIT DROP AS SELECT id FROM scan WHERE score IS NULL LIMIT 1000;
WITH b AS (SELECT id FROM batch) UPDATE scan SET score = 0 FROM b WHERE scan.id = b.id;'
check temp-table-allowed '' 'scan.id' \
'pending 0001_batch'

scripts
script 0001_commit '-- requires: scan.id
UPDATE scan SET score = 0 WHERE score IS NULL;
COMMIT;
BEGIN;'
check transaction-control '' 'scan.id' \
'blocking backfill-ddl 0001_commit line 3: COMMIT
blocking backfill-ddl 0001_commit line 4: BEGIN'

scripts
script 0001_psql '-- requires: scan.id
SELECT count(*) AS n FROM scan \gset
UPDATE scan SET score = 0 WHERE score IS NULL;'
check psql-command '' 'scan.id' \
'blocking backfill-psql-command 0001_psql line 2'

# Hidden in a dollar-quoted body the text check cannot see it; the in-transaction schema check can.
scripts
# shellcheck disable=SC2016 # $body$ is a SQL dollar quote, not a shell expansion
script 0001_hidden '-- requires: scan.id
DO $body$
BEGIN
  EXECUTE '\''ALTER TABLE scan ADD COLUMN extra int'\'';
END $body$;'
check hidden-in-do '' 'scan.id' \
'pending 0001_hidden'

exit "$failed"
