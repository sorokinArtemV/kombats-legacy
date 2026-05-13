#!/usr/bin/env bash
# DNS rotation pre-flight check — Chapter 3 §5 / §13, plan decision Q4.
#
# Verifies that Docker Compose's embedded DNS round-robins the `battle`
# hostname across multiple Battle replicas. Without rotation, every new
# HubConnection from BFF lands on the same replica and the §3.1 math model
# (P(at least one blind) = 0.75 per event) collapses — there's nothing to
# measure. Cited as Step 0 of any multi-replica load test run.
#
# Mechanism:
#   1. Enumerate every container that has the `battle` alias on the kombats
#      Docker network (no hardcoded "battle-1"/"battle-2" — Phase II may use
#      either named services or `--scale battle=N`).
#   2. From a helper alpine container attached to the same network, run
#      `dig +short battle | head -n1` N times — Docker's embedded DNS rotates
#      the order of returned A records, so the *first* IP changes per call
#      when multiple replicas exist.
#   3. Map each first-IP back to its container name and tally hits.
#
# Why first-IP and not full set: .NET's HttpClient (used by BFF's
# BattleHubRelay) picks the first resolved address per new connection, so
# the first-IP distribution is what BFF actually observes.
#
# Exit codes:
#   0 — 2 or more distinct instances detected (DNS rotation confirmed)
#   1 — exactly 1 instance detected (single-replica OR non-rotating DNS)
#   2 — probe failures (network down, helper container failed, etc.)
#
# Tunables via env:
#   BATTLE_SERVICE_NAME  default: battle
#   KOMBATS_NETWORK      default: kombats_default
#   DNS_PROBES           default: 15
#   HELPER_IMAGE         default: alpine:3.20
#
# Portable to bash 3.2 (macOS default) — no associative arrays.

set -euo pipefail

SERVICE="${BATTLE_SERVICE_NAME:-battle}"
NETWORK="${KOMBATS_NETWORK:-kombats_default}"
PROBES="${DNS_PROBES:-15}"
HELPER_IMAGE="${HELPER_IMAGE:-alpine:3.20}"

log()  { printf '%s\n' "$*"; }
fail() { log "ERROR: $*" >&2; exit 2; }

command -v docker >/dev/null 2>&1 || fail "docker CLI not on PATH"

# --- 1. Discover Battle containers (dynamic; no hardcoded names) ----------
log "Discovering containers with alias '${SERVICE}' on network '${NETWORK}'..."

# Parallel arrays — IP_LIST[i] maps to NAME_LIST[i].
IP_LIST=()
NAME_LIST=()

# Helper: look up the index of $1 in IP_LIST, echo -1 if absent.
ip_index() {
    local needle="$1"
    local i=0
    while [ "$i" -lt "${#IP_LIST[@]}" ]; do
        if [ "${IP_LIST[$i]}" = "$needle" ]; then
            echo "$i"
            return
        fi
        i=$((i + 1))
    done
    echo "-1"
}

# Iterate every running container; keep those that advertise SERVICE as a
# network alias on NETWORK.
while IFS= read -r cid; do
    [ -z "$cid" ] && continue
    name=$(docker inspect -f '{{.Name}}' "$cid" 2>/dev/null | sed 's|^/||') || continue
    aliases=$(docker inspect \
        -f "{{with index .NetworkSettings.Networks \"${NETWORK}\"}}{{range .Aliases}}{{println .}}{{end}}{{end}}" \
        "$cid" 2>/dev/null || true)
    if printf '%s\n' "$aliases" | grep -qx "${SERVICE}"; then
        ip=$(docker inspect \
            -f "{{with index .NetworkSettings.Networks \"${NETWORK}\"}}{{.IPAddress}}{{end}}" \
            "$cid" 2>/dev/null || true)
        if [ -n "$ip" ]; then
            IP_LIST+=("$ip")
            NAME_LIST+=("$name")
        fi
    fi
done < <(docker ps -q)

if [ "${#IP_LIST[@]}" -eq 0 ]; then
    fail "no containers found advertising alias '${SERVICE}' on network '${NETWORK}'. Is the stack up? Try: docker compose up -d"
fi

log "Discovered ${#IP_LIST[@]} container(s) with alias '${SERVICE}':"
i=0
while [ "$i" -lt "${#IP_LIST[@]}" ]; do
    log "  ${NAME_LIST[$i]}  ->  ${IP_LIST[$i]}"
    i=$((i + 1))
done

# --- 2. Probe DNS N times from a helper container on the same network ----
log ""
log "Probing 'dig +short ${SERVICE} | head -n1' ${PROBES} times from a helper container on '${NETWORK}'..."

# One docker run; the loop is inside the container so we pay startup cost once.
# Each line of output is the first A record returned for that lookup.
probe_output=$(docker run --rm --network "${NETWORK}" "${HELPER_IMAGE}" sh -c "
    apk add --no-cache bind-tools >/dev/null 2>&1 || exit 50
    for i in \$(seq 1 ${PROBES}); do
        # +tries=1 +retry=0: don't mask flakiness with the resolver's own retry.
        ip=\$(dig +short +tries=1 +retry=0 ${SERVICE} 2>/dev/null | head -n1)
        if [ -z \"\$ip\" ]; then
            echo 'DNS_RESOLUTION_FAILED'
        else
            echo \"\$ip\"
        fi
    done
") || fail "helper container probe failed (exit code $?)"

# --- 3. Tally first-IP hits per instance ---------------------------------
# Parallel arrays for hit counters.
HIT_IPS=()
HIT_COUNTS=()
failed_probes=0
total_probes=0

while IFS= read -r ip; do
    [ -z "$ip" ] && continue
    total_probes=$((total_probes + 1))
    if [ "$ip" = "DNS_RESOLUTION_FAILED" ]; then
        failed_probes=$((failed_probes + 1))
        continue
    fi
    # Find or insert into hit table.
    j=0; found=-1
    while [ "$j" -lt "${#HIT_IPS[@]}" ]; do
        if [ "${HIT_IPS[$j]}" = "$ip" ]; then
            found="$j"
            break
        fi
        j=$((j + 1))
    done
    if [ "$found" -ge 0 ]; then
        HIT_COUNTS[$found]=$((${HIT_COUNTS[$found]} + 1))
    else
        HIT_IPS+=("$ip")
        HIT_COUNTS+=(1)
    fi
done <<< "$probe_output"

log ""
log "Probe results (${total_probes} probes, ${failed_probes} failed):"
if [ "$failed_probes" -gt 0 ] && [ "$failed_probes" -ge "$((total_probes / 2))" ]; then
    log "  too many DNS failures — cannot determine rotation"
    log "ERROR: probe failures, cannot determine"
    exit 2
fi

# Report hits per known instance + any unknown IPs.
distinct_instances=0
k=0
while [ "$k" -lt "${#HIT_IPS[@]}" ]; do
    ip="${HIT_IPS[$k]}"
    count="${HIT_COUNTS[$k]}"
    idx=$(ip_index "$ip")
    if [ "$idx" -ge 0 ]; then
        name="${NAME_LIST[$idx]}"
    else
        name="<unknown:${ip}>"
    fi
    log "  ${name} (${ip}): ${count} hits"
    distinct_instances=$((distinct_instances + 1))
    k=$((k + 1))
done

# --- 4. Verdict -----------------------------------------------------------
log ""
if [ "$distinct_instances" -ge 2 ]; then
    log "OK: DNS rotation confirmed"
    exit 0
elif [ "$distinct_instances" -eq 1 ]; then
    # Distinguish "only one replica exists" from "multiple replicas but DNS sticky".
    if [ "${#IP_LIST[@]}" -le 1 ]; then
        log "STOP: only 1 instance detected (single-replica or non-rotating DNS)"
    else
        log "  WARNING: ${#IP_LIST[@]} replicas exist but only 1 served any probe — DNS is not rotating"
        log "STOP: only 1 instance detected (single-replica or non-rotating DNS)"
    fi
    exit 1
else
    log "ERROR: probe failures, cannot determine"
    exit 2
fi
