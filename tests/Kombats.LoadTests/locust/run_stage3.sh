#!/usr/bin/env bash
# Run 7 Stage 3 RE-RUN — 100 simul, 30s ramp, 300s hold
# Fires locust + 3 infra snapshots mid-hold.
# Adds locust process CPU% sampling at each snapshot.
set -u
LOG_DIR=/tmp/run7_stage3
rm -rf "$LOG_DIR"
mkdir -p "$LOG_DIR"

SCRIPT_T0=$(date +%s)
echo "[$(date '+%H:%M:%S')] T0=${SCRIPT_T0} — launching locust" | tee "$LOG_DIR/timeline.log"

cd "$(dirname "$0")"
RAMP_USERS=100 RAMP_SECONDS=30 HOLD_SECONDS=300 ./.venv/bin/locust \
  -f locustfile.py --headless --only-summary \
  > "$LOG_DIR/locust.out" 2> "$LOG_DIR/locust.err" &
LOCUST_PID=$!
echo "[$(date '+%H:%M:%S')] locust PID=${LOCUST_PID}" | tee -a "$LOG_DIR/timeline.log"

# Resolve the true python locust process — venv launcher may exec into python.
# Snapshot fn re-resolves each time in case PID changes during exec.
resolve_locust_pid() {
  local root=$1
  # Try original PID; if alive, also look for python descendants under it.
  local out="${root}"
  # Find child python processes of the venv tree (may be empty if locust is already python)
  local kids
  kids=$(pgrep -P "$root" 2>/dev/null | head -3 || true)
  echo "${root} ${kids}" | tr ' ' '\n' | grep -v '^$' | sort -u | tr '\n' ' '
}

snapshot() {
  local label=$1
  local out="$LOG_DIR/snapshot_${label}.txt"
  local now=$(date +%s)
  local elapsed=$(( now - SCRIPT_T0 ))
  echo "[$(date '+%H:%M:%S')] snapshot ${label} (T+${elapsed}s)" | tee -a "$LOG_DIR/timeline.log"
  {
    echo "==== snapshot ${label} @ $(date '+%Y-%m-%d %H:%M:%S') (T+${elapsed}s) ===="
    echo "---- docker stats --no-stream ----"
    docker stats --no-stream
    echo "---- Postgres pg_stat_activity count ----"
    docker exec kombats-postgres psql -U postgres -d kombats -c "SELECT count(*) FROM pg_stat_activity;" 2>&1
    echo "---- Redis instantaneous_ops_per_sec + DBSIZE ----"
    docker exec kombats-redis redis-cli INFO stats 2>&1 | grep -E 'instantaneous_ops_per_sec'
    docker exec kombats-redis redis-cli DBSIZE 2>&1
    echo "---- RabbitMQ list_queues name messages ----"
    docker exec kombats-rabbitmq rabbitmqctl list_queues name messages 2>&1
    echo "---- vm_stat ----"
    vm_stat
    echo "---- Locust process CPU% (ps -o avg-since-start) ----"
    local pids
    pids=$(resolve_locust_pid "$LOCUST_PID")
    echo "candidate PIDs: ${pids}"
    for p in $pids; do
      ps -p "$p" -o pid,ppid,pcpu,pmem,etime,command 2>&1 | tail -n +1
    done
    echo "---- Locust process CPU% (top -l 2, instantaneous from 2nd sample) ----"
    # top -l 2 with -pid filter; macOS top syntax. -s 1 = 1s interval between samples.
    top -l 2 -s 1 -n 0 -stats pid,cpu,mem,time,command -pid "$LOCUST_PID" 2>&1 | tail -20
    echo "---- pgrep -laf locust (any other locust processes) ----"
    pgrep -laf 'locust|virtual_player' 2>&1 | head -10
  } > "$out" 2>&1
  echo "[$(date '+%H:%M:%S')] snapshot ${label} done" | tee -a "$LOG_DIR/timeline.log"
}

target_at() {
  local target=$1
  local now=$(date +%s)
  local sleep_s=$(( SCRIPT_T0 + target - now ))
  if (( sleep_s > 0 )); then
    sleep "$sleep_s"
  fi
}

target_at 90;  snapshot 1
target_at 180; snapshot 2
target_at 270; snapshot 3

echo "[$(date '+%H:%M:%S')] waiting for locust to exit (PID=${LOCUST_PID})" | tee -a "$LOG_DIR/timeline.log"
wait "$LOCUST_PID"
LOCUST_EC=$?
echo "[$(date '+%H:%M:%S')] locust exit code=${LOCUST_EC}" | tee -a "$LOG_DIR/timeline.log"
echo "DONE" > "$LOG_DIR/done.marker"
