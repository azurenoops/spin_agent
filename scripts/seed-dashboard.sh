#!/usr/bin/env bash
set -euo pipefail

# Configurable base — defaults to localhost:3001 but honors:
#   - $ATO_BASE_URL  (full URL, including /api/dashboard)
#   - $ATO_SERVER_PORT (just the port; full URL is built from it)
#   - .env in repo root (auto-loaded if neither is set)
if [ -z "${ATO_BASE_URL:-}" ]; then
  if [ -z "${ATO_SERVER_PORT:-}" ] && [ -f "$(dirname "$0")/../.env" ]; then
    # shellcheck disable=SC1090,SC2046
    set -o allexport; . "$(dirname "$0")/../.env"; set +o allexport || true
  fi
  ATO_BASE_URL="http://localhost:${ATO_SERVER_PORT:-3001}/api/dashboard"
fi
BASE="$ATO_BASE_URL"
echo "Seeding via $BASE"
H="Content-Type: application/json"

post() { curl -s -X POST "$1" -H "$H" -d "$2"; }
post_safe() { curl -s -X POST "$1" -H "$H" -d "$2" > /dev/null 2>&1 || true; }
get_id() { python3 -c "import sys,json; print(json.load(sys.stdin)['id'])"; }

# Idempotent helper: find existing capability by name, or create a new one
find_or_create_cap() {
  local name="$1"
  local json="$2"
  # URL-encode the name for search
  local encoded
  encoded=$(python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$name")
  local existing
  existing=$(curl -s "$BASE/capabilities?search=$encoded&pageSize=1")
  local count
  count=$(echo "$existing" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('totalCount',0))" 2>/dev/null || echo "0")
  if [ "$count" -gt "0" ]; then
    local eid
    eid=$(echo "$existing" | python3 -c "import sys,json; print(json.load(sys.stdin)['items'][0]['id'])")
    echo "$eid"
  else
    post "$BASE/capabilities" "$json" | get_id
  fi
}

# Idempotent helper: find existing component by name, or create a new one
find_or_create_comp() {
  local name="$1"
  local json="$2"
  local encoded
  encoded=$(python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$name")
  local existing
  existing=$(curl -s "$BASE/components?search=$encoded&pageSize=1")
  local count
  count=$(echo "$existing" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('totalCount',0))" 2>/dev/null || echo "0")
  if [ "$count" -gt "0" ]; then
    local eid
    eid=$(echo "$existing" | python3 -c "import sys,json; print(json.load(sys.stdin)['items'][0]['id'])")
    echo "$eid"
  else
    post "$BASE/components" "$json" | get_id
  fi
}

echo "=== Creating 12 Capabilities (idempotent) ==="

CAP_MFA=$(find_or_create_cap "Multi-Factor Authentication (MFA)" '{"name":"Multi-Factor Authentication (MFA)","provider":"Microsoft Entra ID","category":"AC","description":"Enforces MFA via Entra ID Conditional Access policies with risk-based sign-in, FIDO2 keys, and Microsoft Authenticator.","implementationStatus":"Implemented","owner":"Identity & Access Management Team"}')
echo "  MFA: $CAP_MFA"

CAP_RBAC=$(find_or_create_cap "Role-Based Access Control (RBAC)" '{"name":"Role-Based Access Control (RBAC)","provider":"Microsoft Entra ID","category":"AC","description":"Least-privilege access via Entra ID RBAC roles, PIM for just-in-time elevation, and periodic access reviews.","implementationStatus":"Implemented","owner":"Identity & Access Management Team"}')
echo "  RBAC: $CAP_RBAC"

CAP_SIEM=$(find_or_create_cap "Security Information & Event Management (SIEM)" '{"name":"Security Information & Event Management (SIEM)","provider":"Microsoft Sentinel","category":"AU","description":"Centralized log collection, correlation, and analysis. Ingests Entra ID, Defender, Key Vault, and NSG logs with automated threat detection.","implementationStatus":"Implemented","owner":"Security Operations Center"}')
echo "  SIEM: $CAP_SIEM"

CAP_CSPM=$(find_or_create_cap "Continuous Security Assessment" '{"name":"Continuous Security Assessment","provider":"Microsoft Defender for Cloud","category":"CA","description":"Automated security posture management via Defender for Cloud secure score, regulatory compliance dashboard, and vulnerability assessments.","implementationStatus":"Implemented","owner":"Cloud Security Team"}')
echo "  CSPM: $CAP_CSPM"

CAP_POLICY=$(find_or_create_cap "Infrastructure Policy Enforcement" '{"name":"Infrastructure Policy Enforcement","provider":"Azure Policy","category":"CM","description":"Enforces organizational standards at scale. Includes deny policies, audit policies for drift detection, and auto-remediation tasks.","implementationStatus":"Implemented","owner":"Platform Engineering Team"}')
echo "  Policy: $CAP_POLICY"

CAP_CERTS=$(find_or_create_cap "Certificate-Based Authentication" '{"name":"Certificate-Based Authentication","provider":"Azure Key Vault","category":"IA","description":"Manages X.509 and TLS certificates via Key Vault with HSM-backed keys. Automated rotation and expiration alerting.","implementationStatus":"Implemented","owner":"PKI & Cryptography Team"}')
echo "  Certs: $CAP_CERTS"

CAP_NETSEG=$(find_or_create_cap "Network Segmentation & Filtering" '{"name":"Network Segmentation & Filtering","provider":"Azure Networking","category":"SC","description":"Defense-in-depth via NSGs, Azure Firewall, Private Endpoints, and VNet service endpoints. Deny-by-default rules.","implementationStatus":"Implemented","owner":"Network Security Team"}')
echo "  NetSeg: $CAP_NETSEG"

CAP_ENCRYPT=$(find_or_create_cap "Data Encryption (At-Rest & In-Transit)" '{"name":"Data Encryption (At-Rest & In-Transit)","provider":"Azure Platform","category":"SC","description":"AES-256 at rest via Storage Service Encryption and SQL TDE. TLS 1.2+ in transit. Customer-managed keys in Key Vault.","implementationStatus":"Implemented","owner":"Data Security Team"}')
echo "  Encrypt: $CAP_ENCRYPT"

CAP_EDR=$(find_or_create_cap "Endpoint Detection & Response (EDR)" '{"name":"Endpoint Detection & Response (EDR)","provider":"Microsoft Defender for Endpoint","category":"SI","description":"Real-time endpoint threat detection, automated investigation, attack surface reduction, and vulnerability management.","implementationStatus":"Implemented","owner":"Security Operations Center"}')
echo "  EDR: $CAP_EDR"

CAP_SOAR=$(find_or_create_cap "Security Orchestration & Automated Response" '{"name":"Security Orchestration & Automated Response","provider":"Microsoft Sentinel","category":"IR","description":"Automated incident response playbooks, enrichment, containment actions, notification workflows, and ServiceNow integration.","implementationStatus":"InProgress","owner":"Security Operations Center"}')
echo "  SOAR: $CAP_SOAR"

CAP_BACKUP=$(find_or_create_cap "Backup & Disaster Recovery" '{"name":"Backup & Disaster Recovery","provider":"Azure Backup & Site Recovery","category":"CP","description":"Automated daily backups with geo-redundant storage. Cross-region replication, RPO < 15 min, RTO < 1 hour.","implementationStatus":"Implemented","owner":"Platform Engineering Team"}')
echo "  Backup: $CAP_BACKUP"

CAP_VULN=$(find_or_create_cap "Vulnerability Management" '{"name":"Vulnerability Management","provider":"Microsoft Defender Vulnerability Management","category":"RA","description":"Continuous vulnerability scanning of OS, apps, containers. Prioritized remediation by exploit likelihood and asset criticality.","implementationStatus":"Implemented","owner":"Vulnerability Management Team"}')
echo "  Vuln: $CAP_VULN"

echo ""
echo "=== Creating 14 Components (8 Things, 3 People, 3 Places — idempotent) ==="

# Things — linked to capabilities
COMP_ENTRA=$(find_or_create_comp "Microsoft Entra ID" "{\"name\":\"Microsoft Entra ID\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Cloud Service\",\"description\":\"Cloud identity and access management: SSO, MFA, conditional access, PIM.\",\"owner\":\"Identity & Access Management Team\",\"linkedCapabilityIds\":[\"$CAP_MFA\",\"$CAP_RBAC\"]}")
echo "  Entra ID: $COMP_ENTRA (linked: MFA, RBAC)"

COMP_SENTINEL=$(find_or_create_comp "Microsoft Sentinel" "{\"name\":\"Microsoft Sentinel\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Cloud Service\",\"description\":\"Cloud-native SIEM/SOAR providing threat detection and automated response.\",\"owner\":\"Security Operations Center\",\"linkedCapabilityIds\":[\"$CAP_SIEM\",\"$CAP_SOAR\"]}")
echo "  Sentinel: $COMP_SENTINEL (linked: SIEM, SOAR)"

COMP_DEFENDER=$(find_or_create_comp "Microsoft Defender for Cloud" "{\"name\":\"Microsoft Defender for Cloud\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Cloud Service\",\"description\":\"Unified CSPM and CWPP: continuous assessment, vuln management, regulatory compliance.\",\"owner\":\"Cloud Security Team\",\"linkedCapabilityIds\":[\"$CAP_CSPM\",\"$CAP_VULN\"]}")
echo "  Defender Cloud: $COMP_DEFENDER (linked: CSPM, Vuln)"

COMP_KV=$(find_or_create_comp "Azure Key Vault" "{\"name\":\"Azure Key Vault\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Cloud Service\",\"description\":\"Centralized secrets, keys, and certificate management with FIPS 140-2 Level 2 HSMs.\",\"owner\":\"PKI & Cryptography Team\",\"linkedCapabilityIds\":[\"$CAP_CERTS\",\"$CAP_ENCRYPT\"]}")
echo "  Key Vault: $COMP_KV (linked: Certs, Encrypt)"

COMP_FW=$(find_or_create_comp "Azure Firewall" "{\"name\":\"Azure Firewall\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Network Appliance\",\"description\":\"Cloud-native L3-L7 firewall with threat intelligence, TLS inspection, and centralized policy.\",\"owner\":\"Network Security Team\",\"linkedCapabilityIds\":[\"$CAP_NETSEG\"]}")
echo "  Firewall: $COMP_FW (linked: NetSeg)"

COMP_DFE=$(find_or_create_comp "Microsoft Defender for Endpoint" "{\"name\":\"Microsoft Defender for Endpoint\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Security Agent\",\"description\":\"Enterprise endpoint protection: detection, investigation, ASR rules, TVM.\",\"owner\":\"Security Operations Center\",\"linkedCapabilityIds\":[\"$CAP_EDR\"]}")
echo "  Defender Endpoint: $COMP_DFE (linked: EDR)"

COMP_ASR=$(find_or_create_comp "Azure Backup & Site Recovery" "{\"name\":\"Azure Backup & Site Recovery\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Cloud Service\",\"description\":\"Enterprise backup and DR: automated policies, geo-redundant storage, cross-region failover.\",\"owner\":\"Platform Engineering Team\",\"linkedCapabilityIds\":[\"$CAP_BACKUP\"]}")
echo "  Backup/DR: $COMP_ASR (linked: Backup)"

COMP_POLICY=$(find_or_create_comp "Azure Policy Engine" "{\"name\":\"Azure Policy Engine\",\"componentType\":\"Thing\",\"status\":\"Active\",\"subType\":\"Governance Service\",\"description\":\"Policy-as-code engine enforcing standards at resource deployment time.\",\"owner\":\"Platform Engineering Team\",\"linkedCapabilityIds\":[\"$CAP_POLICY\"]}")
echo "  Policy Engine: $COMP_POLICY (linked: Policy)"

# People (with personName and email)
COMP_ISSM=$(find_or_create_comp "Information System Security Manager (ISSM)" '{"name":"Information System Security Manager (ISSM)","componentType":"Person","status":"Active","subType":"Security Personnel","description":"Manages system security posture, coordinates assessments, maintains authorization package.","owner":"CISO Office","personName":"Sarah Mitchell","email":"sarah.mitchell@agency.gov","linkedCapabilityIds":[]}')
echo "  ISSM: $COMP_ISSM"

COMP_ISSO=$(find_or_create_comp "Information System Security Officer (ISSO)" '{"name":"Information System Security Officer (ISSO)","componentType":"Person","status":"Active","subType":"Security Personnel","description":"Day-to-day security operations: monitoring, audit log review, vulnerability tracking, incident response.","owner":"CISO Office","personName":"James Rodriguez","email":"james.rodriguez@agency.gov","linkedCapabilityIds":[]}')
echo "  ISSO: $COMP_ISSO"

COMP_ADMIN=$(find_or_create_comp "System Administrator" '{"name":"System Administrator","componentType":"Person","status":"Active","subType":"Technical Personnel","description":"System configuration, patching, account provisioning, backup operations per security baselines.","owner":"IT Operations","personName":"David Kim","email":"david.kim@agency.gov","linkedCapabilityIds":[]}')
echo "  SysAdmin: $COMP_ADMIN"

# Places
COMP_AZVA=$(find_or_create_comp "Azure Government US Gov Virginia" '{"name":"Azure Government US Gov Virginia","componentType":"Place","status":"Active","subType":"Cloud Region","description":"Primary hosting region: FedRAMP High and DoD IL5 accredited infrastructure for all production workloads.","owner":"Platform Engineering Team","linkedCapabilityIds":[]}')
echo "  Azure Gov VA: $COMP_AZVA"

COMP_AZTX=$(find_or_create_comp "Azure Government US Gov Texas" '{"name":"Azure Government US Gov Texas","componentType":"Place","status":"Active","subType":"Cloud Region","description":"Secondary / DR region for geo-redundant backups and cross-region failover.","owner":"Platform Engineering Team","linkedCapabilityIds":[]}')
echo "  Azure Gov TX: $COMP_AZTX"

COMP_AZAZ=$(find_or_create_comp "Azure Government US Gov Arizona" '{"name":"Azure Government US Gov Arizona","componentType":"Place","status":"Active","subType":"Cloud Region","description":"Tertiary region for additional redundancy and data sovereignty requirements.","owner":"Platform Engineering Team","linkedCapabilityIds":[]}')
echo "  Azure Gov AZ: $COMP_AZAZ"

echo ""
echo "=== Resolving System IDs (auto-seeds Eagle Eye / Eagle Nest if missing) ==="

# Idempotent helper: find existing system by exact name, or create a new one
# Returns the system UUID, even if the system already existed.
find_or_create_system() {
  local name="$1"
  local json="$2"
  local encoded
  encoded=$(python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$name")
  local listing
  listing=$(curl -s "$BASE/portfolio?search=$encoded&pageSize=50")
  local existing_id
  existing_id=$(echo "$listing" | python3 -c "
import sys, json
target = sys.argv[1]
try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)
for it in data.get('items', []):
    if it.get('name') == target:
        # Portfolio DTO uses 'systemId'; legacy callers may return 'id'
        print(it.get('systemId') or it.get('id') or '')
        sys.exit(0)
" "$name" 2>/dev/null || echo "")
  if [ -n "$existing_id" ]; then
    echo "$existing_id"
  else
    post "$BASE/systems" "$json" | get_id
  fi
}

EE=$(find_or_create_system "Eagle Eye" '{
  "name": "Eagle Eye",
  "acronym": "EAGLE-EYE",
  "systemType": "MajorApplication",
  "missionCriticality": "MissionCritical",
  "hostingEnvironment": "AzureGovernment",
  "description": "Flagship intelligence-surveillance-reconnaissance (ISR) data fusion platform.",
  "cloudEnvironment": "Government",
  "subscriptionIds": []
}')
echo "  Eagle Eye  : $EE"

EN=$(find_or_create_system "Eagle Nest" '{
  "name": "Eagle Nest",
  "acronym": "EAGLE-NEST",
  "systemType": "Enclave",
  "missionCriticality": "MissionEssential",
  "hostingEnvironment": "AzureGovernment",
  "description": "Operations enclave hosting analytics back-end services for the Eagle Eye platform.",
  "cloudEnvironment": "Government",
  "subscriptionIds": []
}')
echo "  Eagle Nest : $EN"

echo ""
echo "=== Assigning Components to Systems (idempotent — skips existing) ==="

# Eagle Eye — all 14 components
for CID in $COMP_ENTRA $COMP_SENTINEL $COMP_DEFENDER $COMP_KV $COMP_FW $COMP_DFE $COMP_ASR $COMP_POLICY $COMP_ISSM $COMP_ISSO $COMP_ADMIN $COMP_AZVA $COMP_AZTX $COMP_AZAZ; do
  curl -s -X POST "$BASE/components/$CID/assignments" -H "$H" -d "{\"registeredSystemId\":\"$EE\"}" > /dev/null || true
done
echo "  Eagle Eye: 14 components assigned"

# Eagle Nest — 10 components (no backup, policy engine, azaz, aztx)
for CID in $COMP_ENTRA $COMP_SENTINEL $COMP_DEFENDER $COMP_KV $COMP_FW $COMP_DFE $COMP_ISSM $COMP_ISSO $COMP_ADMIN $COMP_AZVA; do
  curl -s -X POST "$BASE/components/$CID/assignments" -H "$H" -d "{\"registeredSystemId\":\"$EN\"}" > /dev/null || true
done
echo "  Eagle Nest: 10 components assigned"

echo ""
echo "=== Creating Control Mappings (idempotent — skips existing) ==="

# Eagle Eye mappings
post_safe "$BASE/capabilities/$CAP_MFA/mappings" "{\"mappings\":[{\"controlId\":\"ac-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ac-7\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ia-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ia-5\",\"role\":\"Supporting\",\"registeredSystemId\":\"$EE\"}]}"
echo "  MFA -> ac-2, ac-7, ia-2, ia-5 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_RBAC/mappings" "{\"mappings\":[{\"controlId\":\"ac-2\",\"role\":\"Supporting\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ac-3\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ac-6\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  RBAC -> ac-2, ac-3, ac-6 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_SIEM/mappings" "{\"mappings\":[{\"controlId\":\"au-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"au-3\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"au-6\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"si-4\",\"role\":\"Supporting\",\"registeredSystemId\":\"$EE\"}]}"
echo "  SIEM -> au-2, au-3, au-6, si-4 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_CSPM/mappings" "{\"mappings\":[{\"controlId\":\"ca-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ca-7\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ra-5\",\"role\":\"Supporting\",\"registeredSystemId\":\"$EE\"}]}"
echo "  CSPM -> ca-2, ca-7, ra-5 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_NETSEG/mappings" "{\"mappings\":[{\"controlId\":\"sc-7\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ac-4\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  NetSeg -> sc-7, ac-4 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_ENCRYPT/mappings" "{\"mappings\":[{\"controlId\":\"sc-8\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"sc-13\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"sc-28\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  Encrypt -> sc-8, sc-13, sc-28 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_CERTS/mappings" "{\"mappings\":[{\"controlId\":\"ia-5\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"sc-12\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  Certs -> ia-5, sc-12 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_EDR/mappings" "{\"mappings\":[{\"controlId\":\"si-3\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"si-4\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  EDR -> si-3, si-4 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_VULN/mappings" "{\"mappings\":[{\"controlId\":\"ra-5\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  Vuln -> ra-5 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_BACKUP/mappings" "{\"mappings\":[{\"controlId\":\"cp-9\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"cp-10\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  Backup -> cp-9, cp-10 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_POLICY/mappings" "{\"mappings\":[{\"controlId\":\"cm-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"cm-6\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  Policy -> cm-2, cm-6 (Eagle Eye)"

post_safe "$BASE/capabilities/$CAP_SOAR/mappings" "{\"mappings\":[{\"controlId\":\"ir-4\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"},{\"controlId\":\"ir-5\",\"role\":\"Primary\",\"registeredSystemId\":\"$EE\"}]}"
echo "  SOAR -> ir-4, ir-5 (Eagle Eye)"

# Eagle Nest mappings (subset)
post_safe "$BASE/capabilities/$CAP_MFA/mappings" "{\"mappings\":[{\"controlId\":\"ac-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"},{\"controlId\":\"ia-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"}]}"
echo "  MFA -> ac-2, ia-2 (Eagle Nest)"

post_safe "$BASE/capabilities/$CAP_SIEM/mappings" "{\"mappings\":[{\"controlId\":\"au-2\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"},{\"controlId\":\"au-6\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"}]}"
echo "  SIEM -> au-2, au-6 (Eagle Nest)"

post_safe "$BASE/capabilities/$CAP_NETSEG/mappings" "{\"mappings\":[{\"controlId\":\"sc-7\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"},{\"controlId\":\"ac-4\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"}]}"
echo "  NetSeg -> sc-7, ac-4 (Eagle Nest)"

post_safe "$BASE/capabilities/$CAP_EDR/mappings" "{\"mappings\":[{\"controlId\":\"si-3\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"},{\"controlId\":\"si-4\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"}]}"
echo "  EDR -> si-3, si-4 (Eagle Nest)"

post_safe "$BASE/capabilities/$CAP_ENCRYPT/mappings" "{\"mappings\":[{\"controlId\":\"sc-28\",\"role\":\"Primary\",\"registeredSystemId\":\"$EN\"}]}"
echo "  Encrypt -> sc-28 (Eagle Nest)"

echo ""
echo "=== Seed Complete ==="
echo "  12 Capabilities (AC, AU, CA, CM, CP, IA, IR, RA, SC, SI)"
echo "  14 Components (8 Things, 3 People, 3 Places)"
echo "  Eagle Eye: 14 components, 32 control mappings, 12 capabilities"
echo "  Eagle Nest: 10 components, 9 control mappings, 5 capabilities"
