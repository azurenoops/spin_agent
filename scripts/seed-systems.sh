#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────
#  Seed five representative test systems via the Dashboard API.
#  Idempotent — each system is created only if a system with the
#  same name does not already exist. Prints UUID for each system.
#
#  Usage:
#    ./scripts/seed-systems.sh
#
#  Honours the same env conventions as seed-dashboard.sh:
#    ATO_BASE_URL  (full base, default http://localhost:${ATO_SERVER_PORT:-3001}/api/dashboard)
#    ATO_SERVER_PORT (just the port; .env is auto-loaded if neither is set)
# ──────────────────────────────────────────────────────────────

set -euo pipefail

if [ -z "${ATO_BASE_URL:-}" ]; then
  if [ -z "${ATO_SERVER_PORT:-}" ] && [ -f "$(dirname "$0")/../.env" ]; then
    set -o allexport; . "$(dirname "$0")/../.env"; set +o allexport || true
  fi
  ATO_BASE_URL="http://localhost:${ATO_SERVER_PORT:-3001}/api/dashboard"
fi
BASE="$ATO_BASE_URL"
H="Content-Type: application/json"

echo "Seeding test systems via $BASE"

# Find an existing system by exact name; echo its id, or empty string.
find_system_id() {
  local name="$1"
  local encoded
  encoded=$(python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$name")
  curl -fsS "$BASE/portfolio?search=$encoded&pageSize=50" 2>/dev/null \
    | python3 -c "
import sys, json
target = sys.argv[1]
try:
    data = json.load(sys.stdin)
except Exception:
    print('')
    sys.exit(0)
for it in data.get('items', []):
    if it.get('name') == target:
        # Portfolio DTO uses 'systemId'; some legacy paths return 'id'
        print(it.get('systemId') or it.get('id') or '')
        sys.exit(0)
print('')
" "$name"
}

# Create-or-find. Echoes the system UUID.
upsert_system() {
  local name="$1"
  local body="$2"
  local existing
  existing=$(find_system_id "$name")
  if [ -n "$existing" ]; then
    echo "$existing"
    return
  fi
  local resp
  resp=$(curl -sS -X POST "$BASE/systems" -H "$H" -d "$body")
  local id
  id=$(echo "$resp" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('id',''))" 2>/dev/null || echo "")
  if [ -z "$id" ]; then
    echo "ERROR creating '$name': $resp" >&2
    return 1
  fi
  echo "$id"
}

echo ""
echo "=== Registering 5 Test Systems (idempotent) ==="

# 1) Eagle Eye — flagship test system referenced by seed-dashboard.sh
EAGLE_EYE=$(upsert_system "Eagle Eye" '{
  "name": "Eagle Eye",
  "acronym": "EAGLE-EYE",
  "systemType": "MajorApplication",
  "missionCriticality": "MissionCritical",
  "hostingEnvironment": "AzureGovernment",
  "description": "Flagship intelligence-surveillance-reconnaissance (ISR) data fusion platform. NIST 800-53 Moderate baseline.",
  "cloudEnvironment": "Government",
  "subscriptionIds": []
}')
echo "  Eagle Eye        : $EAGLE_EYE"

# 2) Eagle Nest — companion enclave
EAGLE_NEST=$(upsert_system "Eagle Nest" '{
  "name": "Eagle Nest",
  "acronym": "EAGLE-NEST",
  "systemType": "Enclave",
  "missionCriticality": "MissionEssential",
  "hostingEnvironment": "AzureGovernment",
  "description": "Operations enclave hosting analytics back-end services for the Eagle Eye platform.",
  "cloudEnvironment": "Government",
  "subscriptionIds": []
}')
echo "  Eagle Nest       : $EAGLE_NEST"

# 3) Phoenix Falcon — second mission-essential MajorApplication
PHOENIX_FALCON=$(upsert_system "Phoenix Falcon" '{
  "name": "Phoenix Falcon",
  "acronym": "PHX-FALCON",
  "systemType": "MajorApplication",
  "missionCriticality": "MissionEssential",
  "hostingEnvironment": "AzureGovernment",
  "description": "Logistics & supply-chain decision-support application. NIST 800-53 Moderate baseline, FedRAMP High overlay.",
  "cloudEnvironment": "Government",
  "subscriptionIds": []
}')
echo "  Phoenix Falcon   : $PHOENIX_FALCON"

# 4) Coastal Watch — admin / mission-support enclave
COASTAL_WATCH=$(upsert_system "Coastal Watch" '{
  "name": "Coastal Watch",
  "acronym": "COASTAL",
  "systemType": "Enclave",
  "missionCriticality": "MissionSupport",
  "hostingEnvironment": "AzureGovernment",
  "description": "Maritime domain awareness collaboration enclave. NIST 800-53 Low baseline.",
  "cloudEnvironment": "Government",
  "subscriptionIds": []
}')
echo "  Coastal Watch    : $COASTAL_WATCH"

# 5) Polar Bear — air-gapped IL6 PlatformIT
POLAR_BEAR=$(upsert_system "Polar Bear" '{
  "name": "Polar Bear",
  "acronym": "POLAR",
  "systemType": "PlatformIt",
  "missionCriticality": "MissionCritical",
  "hostingEnvironment": "AzureGovernmentAirGapped",
  "description": "Air-gapped IL6 platform service providing classified directory and DNS. NIST 800-53 High baseline + CNSSI 1253 overlay.",
  "cloudEnvironment": "GovernmentAirGappedIl6",
  "subscriptionIds": []
}')
echo "  Polar Bear       : $POLAR_BEAR"

echo ""
echo "=== Summary ==="
curl -fsS "$BASE/portfolio?pageSize=50" | python3 -c "
import sys, json
data = json.load(sys.stdin)
print(f\"  Total systems: {data.get('totalCount', 0)}\")
for it in sorted(data.get('items', []), key=lambda x: x.get('name','')):
    print(f\"    - {it.get('name'):16s}  type={it.get('systemType'):16s}  criticality={it.get('missionCriticality'):16s}  phase={it.get('currentRmfPhase')}\")
"

echo ""
echo "Done. To wire components & control mappings to these systems, run:"
echo "    ./scripts/seed-dashboard.sh"
