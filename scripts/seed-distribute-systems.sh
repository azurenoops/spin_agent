#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
#  seed-distribute-systems.sh
#  Distributes demo systems across PMA 290 and PMS 408 tenants.
#
#  Creates:
#    - Eagle Nest      under PMA 290
#    - Phoenix Falcon  under PMA 290
#    - Polar Bear      under PMS 408
#
#  Run AFTER seed-systems.sh (which seeds 5 systems under PEO-790).
#  After this step: PEO-790 = 5 systems, PMA 290 = 2, PMS 408 = 1 (total 8,
#  but seed-systems.sh creates Eagle Nest + Phoenix Falcon + Polar Bear under
#  PEO-790 first; those become the PEO-790 extras if not deleted.  Run this
#  script on a fresh instance that only has PEO-790 populated, or after a wipe.)
#
#  Usage:
#    export ATO_BASE_URL="https://ca-ato-copilot-dashboard-v2.blackwater-9393aa1a.centralus.azurecontainerapps.io/api/dashboard"
#    bash scripts/seed-distribute-systems.sh
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

PYTHON=python3

if [ -z "${ATO_BASE_URL:-}" ]; then
  echo "ERROR: ATO_BASE_URL is not set." >&2
  exit 1
fi

BASE="$ATO_BASE_URL"
H="Content-Type: application/json"
API_ROOT="${ATO_BASE_URL%/api/dashboard}/api"
CSP_DASH_BASE="${ATO_BASE_URL%/api/dashboard}/api/csp/dashboard"

echo "Distributing systems across tenants via $BASE"

# ── Helpers ───────────────────────────────────────────────────────────────────
impersonate_cookie() {
  local tid="$1"
  local headers
  headers=$(curl -sS -i -X POST "$API_ROOT/tenants/$tid/impersonate" -H "$H" 2>/dev/null || true)
  # Collect ALL Set-Cookie values (ato-impersonate + both acaAffinity cookies)
  # and join with "; " — required for Azure Container Apps sticky sessions.
  printf '%s\n' "$headers" \
    | awk 'BEGIN{IGNORECASE=1} /^set-cookie:/ {
        line = $0
        sub(/^set-cookie:[ \t]*/,"",line)
        n = split(line, parts, ";")
        val = parts[1]
        if (length(cookies) > 0) cookies = cookies "; "
        cookies = cookies val
      }
      END { print cookies }'
}

stop_impersonation() {
  local cookie="$1"
  curl -sS -X DELETE "$API_ROOT/tenants/impersonation" \
    -H "$H" -H "Cookie: $cookie" >/dev/null 2>&1 || true
}

# Look up a CSP-managed tenant ID by display name
resolve_tid() {
  local display="$1"
  curl -fsS "$CSP_DASH_BASE/tenants?pageSize=200" 2>/dev/null \
    | $PYTHON -c "
import sys, json
target = sys.argv[1].strip().lower()
try:
    d = json.load(sys.stdin)
    for it in ((d.get('data') or {}).get('items') or []):
        if (it.get('displayName') or '').strip().lower() == target:
            print(it.get('tenantId') or it.get('id') or '')
            break
    else:
        print('')
except Exception:
    print('')
" "$display"
}

# Create a system under an impersonated tenant, idempotent by name
create_system() {
  local name="$1" acronym="$2" sys_type="$3" mission="$4" cookie="$5"
  # Check if it already exists in this tenant
  existing=$( curl -fsS "$BASE/portfolio?pageSize=100&search=$(python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$name" 2>/dev/null || echo "$name")" \
    -H "Cookie: $cookie" 2>/dev/null \
    | $PYTHON -c "
import sys, json
try:
    d = json.load(sys.stdin)
    items = (d.get('items') or [])
    for it in items:
        if (it.get('name') or '').strip().lower() == sys.argv[1].strip().lower():
            print(it.get('systemId') or it.get('id') or '')
            break
    else:
        print('')
except Exception:
    print('')
" "$name" 2>/dev/null || true)

  if [ -n "$existing" ]; then
    echo "  System already exists: $name ($existing)"
    echo "$existing"
    return 0
  fi

  body=$($PYTHON -c "
import json, sys
print(json.dumps({
    'name': sys.argv[1],
    'acronym': sys.argv[2],
    'systemType': sys.argv[3],
    'missionCriticality': sys.argv[4],
    'hostingEnvironment': 'Cloud',
    'cloudEnvironment': 'AzureGovernment',
    'description': 'Demo system seeded by seed-distribute-systems.sh'
}))
" "$name" "$acronym" "$sys_type" "$mission")

  resp=$(curl -sS -X POST "$BASE/systems" -H "$H" -H "Cookie: $cookie" -d "$body")
  sid=$($PYTHON -c "
import sys, json
try:
    d = json.load(sys.stdin)
    print(d.get('id') or d.get('systemId') or '')
except Exception:
    print('')
" <<< "$resp" 2>/dev/null || true)

  if [ -n "$sid" ]; then
    echo "  Created $name -> $sid"
    echo "$sid"
  else
    echo "  WARN: failed to create $name: $resp" >&2
    echo ""
  fi
}

# ── PMA 290 — Eagle Nest + Phoenix Falcon ────────────────────────────────────
echo ""
echo "=== PMA 290 — Eagle Nest + Phoenix Falcon ==="
PMA_TID=$(resolve_tid "PMA 290")
if [ -z "$PMA_TID" ]; then
  echo "ERROR: PMA 290 tenant not found. Run seed-systems.sh first." >&2
  exit 1
fi
echo "  PMA 290 tenant: $PMA_TID"

PMA_COOKIE=$(impersonate_cookie "$PMA_TID")
if [ -z "$PMA_COOKIE" ]; then
  echo "ERROR: Could not impersonate PMA 290 ($PMA_TID)" >&2
  exit 1
fi

EN_ID=$(create_system "Eagle Nest" "EN" "Enclave" "MissionEssential" "$PMA_COOKIE")
PF_ID=$(create_system "Phoenix Falcon" "PF" "MajorApplication" "MissionEssential" "$PMA_COOKIE")

stop_impersonation "$PMA_COOKIE"

# ── PMS 408 — Polar Bear ─────────────────────────────────────────────────────
echo ""
echo "=== PMS 408 — Polar Bear ==="
PMS_TID=$(resolve_tid "PMS 408")
if [ -z "$PMS_TID" ]; then
  echo "ERROR: PMS 408 tenant not found. Run seed-systems.sh first." >&2
  exit 1
fi
echo "  PMS 408 tenant: $PMS_TID"

PMS_COOKIE=$(impersonate_cookie "$PMS_TID")
if [ -z "$PMS_COOKIE" ]; then
  echo "ERROR: Could not impersonate PMS 408 ($PMS_TID)" >&2
  exit 1
fi

PB_ID=$(create_system "Polar Bear" "PB" "PlatformIt" "MissionCritical" "$PMS_COOKIE")

stop_impersonation "$PMS_COOKIE"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "=== Distribution summary ==="
SUMMARY=$(curl -fsS "$CSP_DASH_BASE/summary" 2>/dev/null || echo "{}")
echo "$SUMMARY" | $PYTHON -c "
import sys, json
try:
    d = json.load(sys.stdin)
    data = d.get('data') or {}
    print(f\"  Total orgs:    {data.get('organizationCount', '?')}\")
    print(f\"  Total systems: {data.get('systemCount', '?')}\")
except Exception as e:
    print(f'  (could not parse summary: {e})')
" 2>/dev/null || true

echo ""
echo "Done. Eagle Nest + Phoenix Falcon under PMA 290, Polar Bear under PMS 408."
