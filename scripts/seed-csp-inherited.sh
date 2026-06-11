#!/usr/bin/env bash
# ────────────────────────────────────────────────────────────────────────────
# seed-csp-inherited.sh
#
# Idempotently seeds the CSP-inherited components library (Feature 048 / US9).
# Each component is created at CSP scope (CSP-Admin home tenant) via
# `POST /api/csp/inherited-components` and linked to one or more capabilities
# via `POST /api/csp/inherited-components/{id}/capabilities`. Manual creates
# land as `Published` (CspInheritedComponentService.CreateAsync) so every
# tenant inherits them immediately — no separate `/publish` step required.
#
# Re-runs are safe: components/capabilities are looked up by exact name and
# skipped when they already exist.
#
# Environment:
#   $ATO_BASE_URL     — full base URL (incl. /api). Overrides everything else.
#   $ATO_SERVER_PORT  — port only; URL built as http://localhost:$PORT/api.
#                       Defaults to value in repo-root .env, then 3002.
#
# Pre-requisites:
#   - MCP container running (docker compose up ato-copilot)
#   - Simulated CSP-Admin identity active (appsettings.Development.json:
#     SimulatedRoles includes "CSP.Admin")
#   - CSP profile state = Active (see scripts/seed-systems.sh)
# ────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# ── Resolve Python interpreter (python3 not in PATH on Windows/MSYS) ───────
# Prefer python3 if present; fall back to the Hermes venv python; then plain python.
if command -v python3 >/dev/null 2>&1; then
  PYTHON="python3"
elif [ -f "python3" ]; then
  PYTHON="python3"
elif command -v python >/dev/null 2>&1; then
  PYTHON="python"
else
  echo "✗ No Python interpreter found. Install Python 3 or set up the Hermes venv." >&2
  exit 2
fi
echo "Using Python: $PYTHON ($($PYTHON --version 2>&1))"

if [ -z "${ATO_BASE_URL:-}" ]; then
  if [ -z "${ATO_SERVER_PORT:-}" ] && [ -f "$(dirname "$0")/../.env" ]; then
    # shellcheck disable=SC1090,SC2046
    set -o allexport; . "$(dirname "$0")/../.env"; set +o allexport || true
  fi
  ATO_BASE_URL="http://localhost:${ATO_SERVER_PORT:-3002}/api"
fi

BASE="${ATO_BASE_URL%/}/csp/inherited-components"
H_CT="Content-Type: application/json"

echo "Seeding CSP-inherited components via $BASE"

# ── Pre-flight: confirm endpoint is reachable + CSP profile is Active ─────
preflight() {
  local out status
  out=$(curl -sS -o /tmp/csp_preflight.json -w '%{http_code}' "${BASE}?pageSize=1") || {
    echo "✗ Could not reach $BASE — is the MCP container up?" >&2; exit 2; }
  status="$out"
  case "$status" in
    200) ;;
    403)
      echo "✗ 403 from $BASE — simulated identity is not a CSP.Admin." >&2
      echo "  Check src/Ato.Copilot.Mcp/appsettings.Development.json → SimulatedRoles." >&2
      exit 2 ;;
    404)
      echo "✗ 404 SINGLE_TENANT_MODE — Deployment.Mode must be MultiTenant." >&2
      exit 2 ;;
    503)
      echo "✗ 503 CSP_ONBOARDING_INCOMPLETE — run scripts/seed-systems.sh first." >&2
      exit 2 ;;
    *)
      echo "✗ Unexpected pre-flight status $status from $BASE" >&2
      cat /tmp/csp_preflight.json >&2 || true
      exit 2 ;;
  esac
}

# ── Lookup an existing component by exact name (echo GUID or "") ──────────
find_component_id() {
  local name="$1"
  $PYTHON - "$name" "$BASE" <<'PY' 2>/dev/null || true
import json, sys, urllib.request, urllib.error
name, base = sys.argv[1], sys.argv[2]
try:
    with urllib.request.urlopen(f"{base}?pageSize=500") as r:
        env = json.load(r)
except Exception:
    sys.exit(0)
items = (env.get("data") or {}).get("items") or []
for it in items:
    if it.get("name") == name:
        print(it.get("id", "")); sys.exit(0)
PY
}

# ── Create a component (idempotent on name). Echoes GUID. ─────────────────
create_or_get_component() {
  local name="$1"; local desc="$2"; local ctype="$3"
  local existing
  existing=$(find_component_id "$name")
  if [ -n "$existing" ]; then
    echo "$existing"; return 0
  fi
  local body
  body=$($PYTHON -c '
import json, sys
print(json.dumps({"name": sys.argv[1], "description": sys.argv[2], "componentType": sys.argv[3]}))
' "$name" "$desc" "$ctype")
  local resp
  resp=$(curl -sS -X POST "$BASE" -H "$H_CT" -d "$body")
  echo "$resp" | $PYTHON -c '
import json, sys
env = json.load(sys.stdin)
if env.get("status") != "success":
    sys.stderr.write(f"create component failed: {json.dumps(env)}\n")
    sys.exit(1)
print(env["data"]["id"])
'
}

# ── Lookup an existing capability by exact name (echo GUID or "") ─────────
find_capability_id() {
  local cid="$1"; local cap_name="$2"
  $PYTHON - "$BASE" "$cid" "$cap_name" <<'PY' 2>/dev/null || true
import json, sys, urllib.request
base, cid, name = sys.argv[1], sys.argv[2], sys.argv[3]
try:
    with urllib.request.urlopen(f"{base}/{cid}/capabilities") as r:
        env = json.load(r)
except Exception:
    sys.exit(0)
caps = env.get("data") or []
for c in caps:
    if c.get("name") == name:
        print(c.get("id", "")); sys.exit(0)
PY
}

# ── Add a capability (idempotent on name) ─────────────────────────────────
add_capability() {
  local cid="$1"; local cap_name="$2"; local cap_desc="$3"; local controls_csv="$4"
  local existing
  existing=$(find_capability_id "$cid" "$cap_name")
  if [ -n "$existing" ]; then
    echo "    ↺ $cap_name (exists)"
    return 0
  fi
  local body
  body=$($PYTHON -c '
import json, sys
print(json.dumps({
  "name": sys.argv[1],
  "description": sys.argv[2],
  "mappedNistControlIds": [c.strip() for c in sys.argv[3].split(",") if c.strip()],
}))
' "$cap_name" "$cap_desc" "$controls_csv")
  local resp
  resp=$(curl -sS -X POST "$BASE/$cid/capabilities" -H "$H_CT" -d "$body")
  local newid
  newid=$(echo "$resp" | $PYTHON -c '
import json, sys
env = json.load(sys.stdin)
if env.get("status") != "success":
    sys.stderr.write(f"add capability failed: {json.dumps(env)}\n")
    sys.exit(1)
print(env["data"]["id"])
') || return 1
  echo "    + $cap_name → $newid   [$controls_csv]"
}

# ── Top-level helper: seed one component + its capabilities ───────────────
# Usage: seed "<name>" "<componentType>" "<description>" \
#             "<cap1_name>" "<cap1_desc>" "<cap1_ctrls_csv>" \
#             "<cap2_name>" "<cap2_desc>" "<cap2_ctrls_csv>" ...
seed() {
  local name="$1"; local ctype="$2"; local desc="$3"
  shift 3
  echo "  • $name ($ctype)"
  local cid
  cid=$(create_or_get_component "$name" "$desc" "$ctype")
  echo "    component: $cid"
  while [ $# -gt 0 ]; do
    local cap_name="$1"; local cap_desc="$2"; local cap_ctrls="$3"
    shift 3
    add_capability "$cid" "$cap_name" "$cap_desc" "$cap_ctrls"
  done
}

# ──────────────────────────────── Main ────────────────────────────────────
preflight

echo "=== Creating CSP-inherited components + capabilities (idempotent) ==="

# ── IDENTITY & ACCESS ─────────────────────────────────────────────────────

seed "Microsoft Entra ID" "Identity" \
  "Cloud identity & access management — SSO, MFA, conditional access, PIM." \
  "Multi-Factor Authentication" \
    "Phishing-resistant MFA via Authenticator, FIDO2, and CAC/PIV smart-cards enforced through Entra Conditional Access." \
    "AC-2,AC-7,IA-2,IA-5" \
  "Role-Based Access Control" \
    "Least-privilege access via Entra ID RBAC roles + PIM just-in-time elevation with periodic access reviews." \
    "AC-2,AC-3,AC-6"

seed "Microsoft Entra ID P2" "Identity" \
  "Premium identity protection — risk-based conditional access, Identity Protection, and advanced PIM governance." \
  "Identity Risk Detection" \
    "ML-driven sign-in and user risk policies that block or step-up authentication on anomalous events." \
    "AC-7,IA-2,SI-4" \
  "Access Reviews & Governance" \
    "Automated periodic access reviews for privileged and group memberships with auto-removal of stale access." \
    "AC-2,AC-6,AU-6"

seed "Privileged Identity Management" "Identity" \
  "Just-in-time privileged access with approval workflows, time-limited role activation, and full audit trail." \
  "Just-in-Time Privileged Access" \
    "Eligible role assignments require MFA + manager approval; sessions expire automatically after set duration." \
    "AC-2,AC-3,AC-6" \
  "Privileged Access Audit Trail" \
    "All PIM activations, approvals, and denials logged to Entra audit logs and forwarded to Sentinel." \
    "AU-2,AU-9,AU-12"

# ── SECURITY ──────────────────────────────────────────────────────────────

seed "Microsoft Sentinel" "Service" \
  "Cloud-native SIEM/SOAR — log aggregation, correlation, and automated incident response." \
  "Security Information & Event Management" \
    "Centralized audit log ingestion + analysis across Azure, Entra, M365, and on-prem connectors." \
    "AU-2,AU-3,AU-6,SI-4" \
  "Security Orchestration & Automated Response" \
    "Playbook-driven incident response with ServiceNow / Teams integration and automated containment." \
    "IR-4,IR-5"

seed "Microsoft Defender for Cloud" "Service" \
  "Unified CSPM + CWPP — continuous posture assessment, regulatory compliance, workload protection." \
  "Continuous Security Assessment" \
    "Automated CSPM scoring + Defender for Cloud regulatory compliance dashboard with secure-score trending." \
    "CA-2,CA-7,RA-5" \
  "Vulnerability Management" \
    "Continuous OS / container / app vulnerability scanning prioritized by exploitability and asset criticality." \
    "RA-5,SI-2"

seed "Microsoft Defender for Endpoint" "Service" \
  "Enterprise endpoint EDR — detection, automated investigation, attack-surface reduction, TVM." \
  "Endpoint Detection & Response" \
    "Real-time endpoint threat detection + automated investigation across Windows, Linux, and macOS endpoints." \
    "SI-3,SI-4" \
  "Attack Surface Reduction" \
    "Hardware-based isolation, application control, exploit protection, and controlled folder access policies." \
    "CM-7,SI-3"

seed "Microsoft Defender for Identity" "Service" \
  "Identity threat detection using Active Directory signals — lateral movement, pass-the-hash, golden ticket." \
  "Active Directory Threat Detection" \
    "Behavioral analytics on on-prem AD traffic detect reconnaissance, credential theft, and lateral movement." \
    "SI-4,AU-6,IR-4" \
  "Compromised Account Response" \
    "Automated account disable and password-reset playbooks triggered on high-confidence identity compromise alerts." \
    "IR-4,IR-5,AC-2"

seed "Microsoft Defender for Office 365" "Service" \
  "Email & collaboration threat protection — anti-phishing, safe links, safe attachments, attack simulation." \
  "Anti-Phishing & Email Security" \
    "AI-based anti-phishing, impersonation protection, and spoof intelligence for all inbound mail." \
    "SI-3,SC-7" \
  "Attack Simulation Training" \
    "Tenant-wide phishing simulation campaigns with automated training assignment for susceptible users." \
    "AT-2,AT-3"

# ── INFRASTRUCTURE ────────────────────────────────────────────────────────

seed "Azure Policy Engine" "Platform" \
  "Policy-as-code — deny / audit / auto-remediation enforced at every resource deployment." \
  "Infrastructure Policy Enforcement" \
    "Centrally authored guardrails enforced on every ARM/Bicep deployment with drift auto-remediation." \
    "CM-2,CM-6" \
  "Regulatory Compliance Initiative" \
    "Built-in NIST SP 800-53 R5, DoD IL5, and FedRAMP High policy initiatives with continuous compliance scoring." \
    "CA-2,CA-7,CM-6"

seed "Azure Blueprints" "Platform" \
  "Repeatable governance packages — subscriptions are deployed with pre-approved RBAC, policies, and resources." \
  "Subscription Governance Scaffolding" \
    "Blueprint assignments lock resource groups, apply deny-policies, and pre-provision Log Analytics workspaces." \
    "CM-2,CM-6,AC-3" \
  "Artifact Version Control" \
    "Blueprint definitions are version-controlled and promoted through dev → staging → prod with change approval." \
    "CM-3,CM-5"

seed "Azure Monitor & Log Analytics" "Service" \
  "Full-stack observability — metrics, logs, distributed tracing, and alerting across Azure and hybrid workloads." \
  "Centralized Log Collection" \
    "All Azure resource diagnostic logs, activity logs, and custom app logs streamed to a central Log Analytics workspace." \
    "AU-2,AU-3,AU-12" \
  "Alerting & Anomaly Detection" \
    "Metric alerts, log-query alerts, and smart anomaly detection with PagerDuty / Teams / email notification routing." \
    "AU-6,SI-4"

# ── NETWORK ───────────────────────────────────────────────────────────────

seed "Azure Firewall Premium" "Network" \
  "Cloud-native L3-L7 firewall with threat intelligence, TLS inspection, IDPS, and centralized policy management." \
  "Network Segmentation & Filtering" \
    "Defense-in-depth with NSGs, private endpoints, and deny-by-default rules; centrally authored policy." \
    "SC-7,AC-4" \
  "TLS Inspection & IDPS" \
    "Premium tier deep-packet inspection with signature-based IDPS and TLS termination for east-west traffic." \
    "SC-7,SC-8,SI-4"

seed "Azure DDoS Protection" "Network" \
  "Always-on DDoS mitigation tuned to Azure workloads — volumetric, protocol, and application-layer attack defense." \
  "Volumetric Attack Mitigation" \
    "Automatic traffic profiling and real-time mitigation for L3/L4 floods with SLA-backed response guarantees." \
    "SC-5,SC-7" \
  "DDoS Telemetry & Rapid Response" \
    "Detailed attack telemetry integrated into Azure Monitor with access to DDoS Rapid Response expert team." \
    "IR-4,SI-4"

seed "Azure Private DNS & Private Endpoints" "Network" \
  "Private DNS zones + Private Endpoints eliminate public exposure of PaaS services inside the virtual network." \
  "Private Connectivity to PaaS" \
    "Storage, SQL, Key Vault, and Container Registry accessed over private IP only — no public endpoints." \
    "SC-7,AC-4" \
  "DNS Security" \
    "Private DNS zones prevent DNS hijacking; custom resolvers enforce split-horizon DNS for hybrid workloads." \
    "SC-7,SC-20"

seed "Azure Front Door Premium" "Network" \
  "Global anycast load balancer with WAF, DDoS, caching, and private-origin connectivity for internet-facing apps." \
  "Web Application Firewall" \
    "Managed and custom WAF rule sets block OWASP Top-10 and bot traffic at the edge before reaching origin." \
    "SC-7,SI-3" \
  "Global Traffic Acceleration & HA" \
    "Anycast routing with health probes and automatic failover delivers sub-100ms latency SLA globally." \
    "SC-5,CP-7"

# ── DATA PROTECTION ───────────────────────────────────────────────────────

seed "Azure Key Vault Premium" "Service" \
  "Secrets / keys / certificate management — FIPS 140-3 Level 3 HSMs, fully audit-logged." \
  "Certificate-Based Authentication" \
    "X.509 and TLS certificate lifecycle management with HSM-backed keys, automated rotation, and expiry alerts." \
    "IA-5,SC-12" \
  "Data Encryption (At-Rest & In-Transit)" \
    "Customer-managed encryption keys + TLS 1.2+ enforcement across storage, SQL TDE, and service bus." \
    "SC-8,SC-13,SC-28"

seed "Microsoft Purview" "Service" \
  "Unified data governance — data classification, sensitivity labeling, data loss prevention, and audit." \
  "Data Classification & Labeling" \
    "Automatic and user-applied sensitivity labels on files, emails, and Teams messages based on content patterns." \
    "MP-3,SC-28,AC-16" \
  "Data Loss Prevention" \
    "DLP policies block or warn on exfiltration of CUI, PII, and classified content across M365 and endpoints." \
    "AC-4,SC-8,SI-12"

seed "Azure Information Protection" "Service" \
  "Rights-based document protection — encryption and access policies travel with files outside the tenant boundary." \
  "Persistent Document Encryption" \
    "AIP labels apply persistent AES-256 encryption; access is revoked server-side even after document distribution." \
    "SC-13,SC-28,AC-4" \
  "External Sharing Controls" \
    "B2B guest access and external sharing governed by AIP + Entra Conditional Access with expiry and audit." \
    "AC-3,AC-20,AU-2"

# ── COMPUTE ───────────────────────────────────────────────────────────────

seed "Azure Kubernetes Service (AKS)" "Compute" \
  "Managed Kubernetes for containerized workloads — private clusters, RBAC, network policies, and image scanning." \
  "Container Workload Isolation" \
    "Private AKS clusters with Azure CNI, network policies, and pod identity enforce workload micro-segmentation." \
    "SC-7,AC-4,CM-7" \
  "Container Image Security" \
    "Defender for Containers scans images in ACR and at runtime; admission control blocks non-compliant images." \
    "RA-5,CM-7,SI-3"

seed "Azure App Service (PaaS)" "Compute" \
  "Managed web application hosting — private endpoints, managed identity, TLS, and deployment slot governance." \
  "Secure Web Application Hosting" \
    "App Service Environments (ASE) in VNet with private endpoints, managed identity, and Key Vault integration." \
    "SC-7,IA-3,SC-28" \
  "Deployment Governance" \
    "Deployment slots with traffic split, rollback capability, and CI/CD gate approvals via Azure DevOps." \
    "CM-3,CM-5"

# ── STORAGE & DISASTER RECOVERY ───────────────────────────────────────────

seed "Azure Backup & Site Recovery" "Service" \
  "Automated backup + geo-redundant disaster recovery — cross-region failover, RPO < 15m, RTO < 1h." \
  "Backup & Disaster Recovery" \
    "Policy-driven backup with geo-redundant storage and quarterly DR-failover tests." \
    "CP-9,CP-10" \
  "Ransomware-Resistant Backup" \
    "Soft-delete, immutable vault policies, and MUA (multi-user authorization) prevent backup tampering." \
    "CP-9,SI-3"

seed "Azure Storage (GRS/RA-GZRS)" "Storage" \
  "Object, file, and block storage with geo-redundant replication, encryption, and immutability policies." \
  "Geo-Redundant Data Durability" \
    "Read-access geo-zone-redundant storage (RA-GZRS) replicates data across paired regions for 16-nines durability." \
    "CP-6,CP-9" \
  "Immutable Storage & Compliance" \
    "WORM (write-once-read-many) policies on Blob Storage for audit log and evidence immutability." \
    "AU-9,AU-10,CP-9"

# ── COMPLIANCE & GOVERNANCE ───────────────────────────────────────────────

seed "Microsoft Compliance Manager" "Service" \
  "Risk-based compliance score, control mapping to NIST/CMMC/FedRAMP, and assessment workflow management." \
  "Compliance Score & Gap Analysis" \
    "Automated evidence collection from M365 and Azure services mapped to NIST SP 800-53 controls with score trending." \
    "CA-2,CA-7,PL-2" \
  "Assessment Action Management" \
    "Improvement actions assigned to owners with due dates, evidence upload, and status tracking in Compliance Manager." \
    "CA-2,CA-5,PM-6"

seed "Azure Government Cloud Region" "Infrastructure" \
  "FedRAMP High / DoD IL5 accredited hosting infrastructure (US Gov Virginia, Texas, Arizona regions)." \
  "Physical & Environmental Protection" \
    "Tier-IV data-centers with biometric access, redundant power/cooling, and DoD-cleared personnel." \
    "PE-2,PE-3,PE-13" \
  "Sovereign Data Residency" \
    "All data at rest and in transit remains within US Government cloud boundary; screened US persons only." \
    "SA-9,SC-28,AC-20"

echo ""
echo "=== Done ==="
echo "Re-run any time — names are unique keys; existing rows are skipped."
echo "View at: http://localhost:5173/components (as CSP-Admin, NOT impersonating)"
