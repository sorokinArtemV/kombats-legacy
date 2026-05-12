#!/usr/bin/env bash
# Aggregate per-phase latency from a load-test iterations-*.jsonl file.
#
# Usage:
#   ./aggregate-phases.sh path/to/iterations-2026-05-11--12-09-10.jsonl
#
# Output: three slices.
#   1. Overall   — all iterations
#   2. Per-outcome — split by Won / Lost / Draw / QueueTimeout / Error / BattleTimeout
#   3. Successful vs QueueTimeout — Won+Lost+Draw merged vs QueueTimeout only
#
# Requires: jq.

set -euo pipefail

FILE="${1:-}"
if [[ -z "$FILE" ]]; then
  echo "usage: $0 <iterations.jsonl>" >&2
  exit 2
fi
if [[ ! -f "$FILE" ]]; then
  echo "not found: $FILE" >&2
  exit 2
fi

PHASES=(auth_ms onboard_ms connect_ms queue_wait_ms join_battle_ms battle_ms total_ms)

# Percentile + max from a sorted ascending list of numbers.
#   pctile(.; 50) -> p50 (linear-ish, nearest-rank, good enough at this scale).
JQ_STATS='
  def pct(arr; p):
    if (arr | length) == 0 then null
    else (arr | sort) as $s
      | $s[ ((($s | length) - 1) * p / 100) | floor ]
    end;
  def fmt(n): if n == null then "n/a" else (n | tonumber | . * 10 | round / 10 | tostring) end;
  . as $rows
  | reduce $phases[] as $p ({}; . + {
      ($p): {
        count: ($rows | map(.[$p]) | length),
        p50:  pct(($rows | map(.[$p])); 50),
        p95:  pct(($rows | map(.[$p])); 95),
        p99:  pct(($rows | map(.[$p])); 99),
        max:  ($rows | map(.[$p]) | max)
      }
    })
'

render_table() {
  local title="$1"
  shift
  local rows="$1"
  shift
  if [[ -z "$rows" || "$rows" == "[]" ]]; then
    echo "## $title"
    echo "(no iterations matched)"
    echo
    return
  fi
  echo "## $title"
  printf "%-16s %8s %10s %10s %10s %10s\n" "phase" "count" "p50_ms" "p95_ms" "p99_ms" "max_ms"
  local stats
  stats=$(echo "$rows" | jq -c --argjson phases "$(printf '%s\n' "${PHASES[@]}" | jq -R . | jq -s .)" "$JQ_STATS")
  for phase in "${PHASES[@]}"; do
    read -r count p50 p95 p99 max < <(
      echo "$stats" | jq -r --arg p "$phase" '
        .[$p]
        | [
            (.count // 0),
            (if .p50 == null then "n/a" else (.p50 | . * 10 | round / 10 | tostring) end),
            (if .p95 == null then "n/a" else (.p95 | . * 10 | round / 10 | tostring) end),
            (if .p99 == null then "n/a" else (.p99 | . * 10 | round / 10 | tostring) end),
            (if .max == null then "n/a" else (.max | . * 10 | round / 10 | tostring) end)
          ] | @tsv
      '
    )
    printf "%-16s %8s %10s %10s %10s %10s\n" "$phase" "$count" "$p50" "$p95" "$p99" "$max"
  done
  echo
}

# ---- read once into memory ----
ROWS_ALL=$(jq -cs '.' "$FILE")
TOTAL=$(echo "$ROWS_ALL" | jq 'length')

echo "# Phase breakdown: $FILE"
echo "Iterations: $TOTAL"
echo

# ---- Slice 1: overall ----
render_table "1. Overall (all iterations)" "$ROWS_ALL"

# ---- Slice 2: per outcome ----
OUTCOMES=$(echo "$ROWS_ALL" | jq -r '[.[].outcome] | unique | .[]')
for outcome in $OUTCOMES; do
  ROWS=$(echo "$ROWS_ALL" | jq -c "map(select(.outcome == \"$outcome\"))")
  count=$(echo "$ROWS" | jq 'length')
  render_table "2. Outcome: $outcome ($count iterations)" "$ROWS"
done

# ---- Slice 3: successful vs queue-timeout ----
SUCCESS=$(echo "$ROWS_ALL" | jq -c 'map(select(.outcome == "Won" or .outcome == "Lost" or .outcome == "Draw"))')
QTIME=$(echo "$ROWS_ALL" | jq -c 'map(select(.outcome == "QueueTimeout"))')
SUCCESS_N=$(echo "$SUCCESS" | jq 'length')
QTIME_N=$(echo "$QTIME" | jq 'length')
render_table "3a. Successful — Won+Lost+Draw ($SUCCESS_N iterations)" "$SUCCESS"
render_table "3b. QueueTimeout only ($QTIME_N iterations)" "$QTIME"
