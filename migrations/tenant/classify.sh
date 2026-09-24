#!/bin/sh
# Expand-phase gate. Classifies one tenant's diff into what may be applied now (expand), what is
# left for the contract phase (deferred), and what would break the running app (blocking).
#
#   classify.sh <expand.sql> <full.sql> <live-not-null.txt>
#
#   expand.sql         atlas_diff with --env expand (additive changes only) — the plan to apply
#   full.sql           atlas_diff with no policy — used only to see what expand.sql left out
#   live-not-null.txt  "table.column" per line: live columns that are NOT NULL with no default
#
# Prints {"expand":[{table,sql}], "deferred":[{rule,table,sql}], "blocking":[{rule,table,sql}]}.
# Expects Atlas's Postgres output: one statement per ';', ALTER TABLE changes joined with ", ".
set -eu

[ $# -eq 3 ] || { echo "usage: classify.sh <expand.sql> <full.sql> <live-not-null.txt>" >&2; exit 2; }
# awk's getline reads a missing file as empty, which would look like "no changes" — fail instead.
for f in "$@"; do [ -r "$f" ] || { echo "classify.sh: cannot read $f" >&2; exit 2; }; done

awk -v expand_file="$1" -v full_file="$2" -v notnull_file="$3" '
function unquote(s) { gsub(/"/, "", s); sub(/.*\./, "", s); return s }

# Splits every statement in a file into one clause per line: n[file], tbl[file, i], cl[file, i].
# ALTER TABLE t A, B  ->  (t, A), (t, B).  Splitting on ", ADD|DROP|ALTER|RENAME " leaves commas
# inside types such as numeric(10,2) alone.
function load(file, key,    line, buf, stmt, t, rest) {
  n[key] = 0; buf = ""
  while ((getline line < file) > 0) {
    if (line ~ /^[[:space:]]*(--.*)?$/) continue
    buf = buf (buf == "" ? "" : " ") line
    if (buf !~ /;[[:space:]]*$/) continue
    stmt = buf; buf = ""
    sub(/;[[:space:]]*$/, "", stmt); sub(/^[[:space:]]+/, "", stmt)
    if (match(stmt, /^ALTER TABLE [^ ]+ /)) {
      t = unquote(substr(stmt, 13, RLENGTH - 13))
      rest = substr(stmt, RLENGTH + 1)
      while (match(rest, /, (ADD|DROP|ALTER|RENAME) /)) {
        add(key, t, substr(rest, 1, RSTART - 1))
        rest = substr(rest, RSTART + 2)
      }
      add(key, t, rest)
    } else if (match(stmt, /^CREATE TABLE [^ ]+/)) {
      add(key, unquote(substr(stmt, 14, RLENGTH - 13)), stmt)
    } else if (match(stmt, / ON [^ ]+/)) {
      add(key, unquote(substr(stmt, RSTART + 4, RLENGTH - 4)), stmt)
    } else if (match(stmt, /^DROP TABLE [^ ]+/)) {
      add(key, unquote(substr(stmt, 12, RLENGTH - 11)), stmt)
    } else {
      add(key, "-", stmt)
    }
  }
  close(file)
}
function add(key, t, c) { n[key]++; tbl[key, n[key]] = t; cl[key, n[key]] = c }
function out(class, rule, t, c) { printf "%s\t%s\t%s\t%s\n", class, rule, t, c }

BEGIN {
  while ((getline line < notnull_file) > 0) if (line != "") notnull[line] = 1
  close(notnull_file)
  load(expand_file, "e")
  load(full_file, "f")

  # --- the plan itself: only purely additive, backward-compatible clauses may pass ---
  for (i = 1; i <= n["e"]; i++) {
    t = tbl["e", i]; c = cl["e", i]
    if (c ~ /^ADD COLUMN /) {
      added[t] = 1
      if (c ~ / NOT NULL/ && c !~ / DEFAULT /)
        out("blocking", "not-null-without-default", t, c)   # old code inserts without it
      else if (c ~ / DEFAULT .*(gen_random_uuid|uuid_generate_v[14]|random|clock_timestamp|timeofday)\(/)
        out("blocking", "volatile-default", t, c)           # rewrites the whole table
      else
        out("expand", "", t, c)
    } else if (c ~ /^CREATE TABLE / || c ~ /^CREATE (UNIQUE )?INDEX / || c ~ /^COMMENT ON / \
               || c ~ /^ADD CONSTRAINT [^ ]+ FOREIGN KEY /) {
      out("expand", "", t, c)
    } else {
      out("blocking", "not-additive", t, c)                 # the skip policy let something through
    }
  }

  # --- what expand left out: contract work, unless the app would break in the meantime ---
  for (i = 1; i <= n["f"]; i++) {
    t = tbl["f", i]; c = cl["f", i]
    if (t == "__EFMigrationsHistory" || c ~ /__EFMigrationsHistory/) continue  # EF bookkeeping, not model
    if (c ~ /^(CREATE|ADD|COMMENT) /) continue                                # already in expand
    if (match(c, /^DROP COLUMN [^ ]+/)) {
      col = unquote(substr(c, 13, RLENGTH - 12))
      if (t in added)
        out("blocking", "suspected-rename", t, c)       # applying only the ADD would lose the data
      else if ((t "." col) in notnull)
        out("blocking", "drop-not-null-column", t, c)   # new code inserts without it
      else
        out("deferred", "drop-column", t, c)
    } else if (c ~ /^ALTER COLUMN [^ ]+ DROP NOT NULL/) {
      out("blocking", "relax-not-null", t, c)           # model allows NULL, DB still rejects it
    } else {
      out("deferred", "contract", t, c)
    }
  }
}' | jq -Rn '
  [inputs | split("\t") | {class: .[0], rule: .[1], table: .[2], sql: .[3]}]
  | { expand:   map(select(.class == "expand")   | {table, sql}),
      deferred: map(select(.class == "deferred") | {rule, table, sql}),
      blocking: map(select(.class == "blocking") | {rule, table, sql}) }'
