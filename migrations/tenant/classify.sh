#!/bin/sh
# Expand-phase gate. Classifies one tenant's diff into what may be applied now (expand), what is
# left for the contract phase (deferred), and what would break the running app (blocking).
#
#   classify.sh <expand.sql> <full.sql> <live-not-null.txt> <known-drops.txt>
#
#   expand.sql         atlas_diff with --env expand (additive changes only) — the plan to apply
#   full.sql           atlas_diff with no policy — used only to see what expand.sql left out
#   live-not-null.txt  "table.column" per line: live columns that are NOT NULL with no default
#   known-drops.txt    one statement per line: items already pending contract from an earlier model
#                      version (state/pending-contract.json). A column removed back then is not
#                      half of a rename made now.
#
# Prints {"expand":[{table,sql}], "deferred":[{rule,table,sql}], "blocking":[{rule,table,sql}]}.
# Every sql is a statement that runs on its own (ALTER TABLE clauses carry their table), so the plan is
# built from the expand list — it also holds statements Atlas's expand policy can't emit (DROP NOT NULL).
# Expects Atlas's Postgres output: one statement per ';', ALTER TABLE changes joined with ", ".
set -eu

[ $# -eq 4 ] || { echo "usage: classify.sh <expand.sql> <full.sql> <live-not-null.txt> <known-drops.txt>" >&2; exit 2; }
# awk's getline reads a missing file as empty, which would look like "no changes" — fail instead.
for f in "$@"; do [ -r "$f" ] || { echo "classify.sh: cannot read $f" >&2; exit 2; }; done

awk -v expand_file="$1" -v full_file="$2" -v notnull_file="$3" -v known_file="$4" '
function unquote(s) { gsub(/"/, "", s); sub(/.*\./, "", s); return s }

# Splits every statement in a file into one clause per line: n[file], tbl[file, i], cl[file, i], and
# the clause as a statement of its own, st[file, i].
# ALTER TABLE t A, B  ->  (t, A, "ALTER TABLE t A"), (t, B, "ALTER TABLE t B").  Splitting on
# ", ADD|DROP|ALTER|RENAME " leaves commas inside types such as numeric(10,2) alone.
function load(file, key,    line, buf, stmt, t, target, rest) {
  n[key] = 0; buf = ""
  while ((getline line < file) > 0) {
    if (line ~ /^[[:space:]]*(--.*)?$/) continue
    buf = buf (buf == "" ? "" : " ") line
    if (buf !~ /;[[:space:]]*$/) continue
    stmt = buf; buf = ""
    sub(/;[[:space:]]*$/, "", stmt); sub(/^[[:space:]]+/, "", stmt)
    if (match(stmt, /^ALTER TABLE [^ ]+ /)) {
      target = substr(stmt, 1, RLENGTH)  # "ALTER TABLE <schema>.<table> ", exactly as Atlas wrote it
      t = unquote(substr(stmt, 13, RLENGTH - 13))
      rest = substr(stmt, RLENGTH + 1)
      while (match(rest, /, (ADD|DROP|ALTER|RENAME) /)) {
        add(key, t, substr(rest, 1, RSTART - 1), target)
        rest = substr(rest, RSTART + 2)
      }
      add(key, t, rest, target)
    } else if (match(stmt, /^CREATE TABLE [^ ]+/)) {
      add(key, unquote(substr(stmt, 14, RLENGTH - 13)), stmt, "")
    } else if (match(stmt, / ON [^ ]+/)) {
      add(key, unquote(substr(stmt, RSTART + 4, RLENGTH - 4)), stmt, "")
    } else if (match(stmt, /^DROP TABLE [^ ]+/)) {
      add(key, unquote(substr(stmt, 12, RLENGTH - 11)), stmt, "")
    } else {
      add(key, "-", stmt, "")
    }
  }
  close(file)
}
function add(key, t, c, target) { n[key]++; tbl[key, n[key]] = t; cl[key, n[key]] = c; st[key, n[key]] = target c }
function out(class, rule, t, s) { printf "%s\t%s\t%s\t%s\n", class, rule, t, s }

BEGIN {
  while ((getline line < notnull_file) > 0) if (line != "") notnull[line] = 1
  close(notnull_file)
  while ((getline line < known_file) > 0) if (line != "") known[line] = 1
  close(known_file)
  load(expand_file, "e")
  load(full_file, "f")

  # --- the plan itself: only purely additive, backward-compatible clauses may pass ---
  for (i = 1; i <= n["e"]; i++) {
    t = tbl["e", i]; c = cl["e", i]; s = st["e", i]
    if (c ~ /^ADD COLUMN /) {
      added[t] = 1
      if (c ~ / NOT NULL/ && c !~ / DEFAULT /)
        out("blocking", "not-null-without-default", t, s)   # old code inserts without it
      else if (c ~ / DEFAULT .*(gen_random_uuid|uuid_generate_v[14]|random|clock_timestamp|timeofday)\(/)
        out("blocking", "volatile-default", t, s)           # rewrites the whole table
      else
        out("expand", "", t, s)
    } else if (c ~ /^CREATE TABLE / || c ~ /^CREATE (UNIQUE )?INDEX / || c ~ /^COMMENT ON / \
               || c ~ /^ADD CONSTRAINT [^ ]+ FOREIGN KEY /) {
      out("expand", "", t, s)
    } else {
      out("blocking", "not-additive", t, s)                 # the skip policy let something through
    }
  }

  # --- what expand left out: contract work, unless the app would break in the meantime ---
  # Dropping NOT NULL is backward compatible (the old code always writes the column), so it runs
  # in expand: the new code can then leave the column out before the contract phase drops it.
  for (i = 1; i <= n["f"]; i++) {
    t = tbl["f", i]; c = cl["f", i]; s = st["f", i]
    if (t == "__EFMigrationsHistory" || c ~ /__EFMigrationsHistory/) continue  # EF bookkeeping, not model
    if (c ~ /^(CREATE|ADD|COMMENT) /) continue                                # already in expand
    if (match(c, /^DROP COLUMN [^ ]+/)) {
      col = substr(c, 13, RLENGTH - 12)
      if ((t in added) && !(s in known)) {
        out("blocking", "suspected-rename", t, s)       # applying only the ADD would lose the data
      } else {
        if ((t "." unquote(col)) in notnull)            # new code inserts without it
          out("expand", "", t, substr(s, 1, length(s) - length(c)) "ALTER COLUMN " col " DROP NOT NULL")
        out("deferred", "drop-column", t, s)
      }
    } else if (c ~ /^ALTER COLUMN [^ ]+ DROP NOT NULL/) {
      out("expand", "", t, s)                           # model allows NULL, DB still rejects it
    } else if (c ~ /^ALTER COLUMN [^ ]+ TYPE /) {
      out("blocking", "type-change", t, s)              # new code writes values the old type may not hold
    } else {
      out("deferred", "contract", t, s)
    }
  }
}' | jq -Rn '
  [inputs | split("\t") | {class: .[0], rule: .[1], table: .[2], sql: .[3]}]
  | { expand:   map(select(.class == "expand")   | {table, sql}),
      deferred: map(select(.class == "deferred") | {rule, table, sql}),
      blocking: map(select(.class == "blocking") | {rule, table, sql}) }'
