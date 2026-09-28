#!/bin/sh
# Migrate-phase gate. Decides which backfill scripts one tenant still needs, and rejects scripts that
# do more than change data.
#
#   backfills.sh <backfills dir> <backfills.tsv> <columns.txt>
#
#   backfills dir   migrations/tenant/backfills: NNNN_<name>.sql, run in file-name order
#   backfills.tsv   "id<TAB>sha256" per script already run on the tenant (inspect.sh; empty if none)
#   columns.txt     "table.column" for every column the tenant has (inspect.sh)
#
# Prints {"pending":[{id,sha256}], "done":[id], "skipped":[{id,missing}], "blocking":[{rule,id,detail}]}.
#
# Every script starts with "-- requires: <table.column>[, ...]" — the columns it reads or writes. A
# tenant missing one of them never had the old shape (it was provisioned later), so the script is
# skipped there. Only data statements are allowed; anything else blocks the plan with its line
# number. This is a readable early check — the migrate plan re-checks the schema inside the
# transaction, which also catches DDL hidden in a DO block.
set -eu

[ $# -eq 3 ] || { echo "usage: backfills.sh <backfills dir> <backfills.tsv> <columns.txt>" >&2; exit 2; }
dir=$1 record=$2 columns=$3
# A missing file would read as "nothing run yet" / "no columns" — fail instead.
for f in "$record" "$columns"; do [ -r "$f" ] || { echo "backfills.sh: cannot read $f" >&2; exit 2; }; done

# Prints "<rule><TAB><detail>" per statement that is not a data statement, and per psql backslash
# command (it could overwrite the variables the migrate plan checks the schema with). Comments,
# quoted strings, quoted identifiers and dollar-quoted bodies are skipped, so a semicolon or a word
# like DROP inside them is not a statement.
check_statements() { # file
  awk '
  function flush(   s, w) {
    s = toupper(stmt); gsub(/^[[:space:]]+|[[:space:]]+$/, "", s)
    if (s != "") {
      w = s; sub(/[^A-Z].*$/, "", w)
      if (!(w in data) && s !~ /^CREATE[[:space:]]+TEMP(ORARY)?[[:space:]]+TABLE[[:space:]]/)
        printf "backfill-ddl\tline %d: %s\n", start, (w != "" ? w : substr(s, 1, 20))
    }
    stmt = ""; start = 0
  }
  # Skips len characters of src from i, counting the newlines in them; the statement gets filler
  # in their place (a word for a string, a space for a comment).
  function skip(len, filler,   s) { s = substr(src, i, len); line += gsub(/\n/, "", s); i += len; stmt = stmt filler }
  BEGIN { split("SELECT INSERT UPDATE DELETE MERGE WITH DO", a, " "); for (k in a) data[a[k]] = 1; q = sprintf("%c", 39) }
  { src = src $0 "\n" }
  END {
    n = length(src); i = 1; line = 1; stmt = ""; start = 0
    while (i <= n) {
      c = substr(src, i, 1); rest = substr(src, i)
      if (substr(rest, 1, 2) == "--") {
        j = index(rest, "\n"); i += (j ? j - 1 : length(rest))
      } else if (substr(rest, 1, 2) == "/*") {
        j = index(substr(rest, 3), "*/"); skip(j ? j + 3 : length(rest), " ")
      } else if (c == q || c == "\"") {
        if (!start) start = line
        j = index(substr(rest, 2), c); skip(j ? j + 1 : length(rest), " x ")
      } else if (c == "$" && match(rest, /^\$([A-Za-z_][A-Za-z_0-9]*)?\$/)) {
        if (!start) start = line
        tl = RLENGTH; j = index(substr(rest, tl + 1), substr(rest, 1, tl))
        skip(j ? j - 1 + 2 * tl : length(rest), " x ")
      } else if (c == "\\") {
        printf "backfill-psql-command\tline %d\n", line; i++
      } else if (c == ";") {
        flush(); i++
      } else {
        if (c == "\n") line++
        else if (!start && c !~ /[[:space:]]/) start = line
        stmt = stmt c; i++
      }
    }
    # The migrate plan appends the record row right after the script.
    if (stmt ~ /[^[:space:]]/) printf "backfill-unterminated\tline %d: end the last statement with ;\n", start
    flush()
  }' "$1"
}

out() { printf '%s\t%s\t%s\t%s\t%s\n' "$@"; } # class rule id sha256 detail
TAB=$(printf '\t')

{
  for file in "$dir"/*.sql; do
    [ -e "$file" ] || continue # no scripts
    id=$(basename "$file" .sql)
    sha=$(sha256sum "$file" | cut -d' ' -f1)

    # The id goes into the record table and orders the scripts: NNNN_ and nothing to quote.
    if ! printf '%s' "$id" | grep -Eq '^[0-9]{4}_[A-Za-z0-9_]+$'; then
      out blocking backfill-bad-name "$id" "$sha" "name it NNNN_<name>.sql"
      continue
    fi
    problems=$(check_statements "$file")
    requires=$(sed -n 's/^--[[:space:]]*requires:[[:space:]]*//p' "$file" | head -n 1 | tr ',' ' ')
    case $requires in
      *[![:space:]]*) ;;
      *) problems="${problems:+$problems
}backfill-no-requires${TAB}add a line: -- requires: <table.column>, ..." ;;
    esac
    if [ -n "$problems" ]; then
      printf '%s\n' "$problems" | while IFS="$TAB" read -r rule detail; do
        out blocking "$rule" "$id" "$sha" "$detail"
      done
      continue
    fi

    recorded=$(awk -F '\t' -v id="$id" '$1 == id { print $2 }' "$record")
    if [ -n "$recorded" ]; then
      if [ "$recorded" = "$sha" ]; then
        out "done" - "$id" "$sha" ""
      else
        out blocking backfill-changed "$id" "$sha" "edited after it ran — add a new script instead"
      fi
      continue
    fi

    missing=""
    for col in $requires; do
      grep -qxF "$col" "$columns" || missing="${missing:+$missing }$col"
    done
    if [ -n "$missing" ]; then
      out skipped - "$id" "$sha" "$missing"
    else
      out pending - "$id" "$sha" ""
    fi
  done
} | jq -Rn '
  [inputs | split("\t") | {class: .[0], rule: .[1], id: .[2], sha256: .[3], detail: .[4]}]
  | { pending:  map(select(.class == "pending")  | {id, sha256}),
      done:     map(select(.class == "done")     | .id),
      skipped:  map(select(.class == "skipped")  | {id, missing: (.detail | split(" "))}),
      blocking: map(select(.class == "blocking") | {rule, id, detail}) }'
