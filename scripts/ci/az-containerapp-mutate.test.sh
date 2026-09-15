#!/usr/bin/env bash
# Local checks for wait_containerapp_idle (no Azure). Run from repo root:
#   bash scripts/ci/az-containerapp-mutate.test.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/ci/az-containerapp-mutate.sh
source "${SCRIPT_DIR}/az-containerapp-mutate.sh"

az() {
  echo "${MOCK_PROVISIONING_STATE}"
}

assert_idle() {
  local state="$1"
  MOCK_PROVISIONING_STATE="${state}"
  if ! wait_containerapp_idle "rg" "app" 1 0; then
    echo "FAIL: provisioningState=${state} should be idle (proceed)."
    exit 1
  fi
}

assert_timeout() {
  local state="$1"
  MOCK_PROVISIONING_STATE="${state}"
  if wait_containerapp_idle "rg" "app" 1 0; then
    echo "FAIL: provisioningState=${state} should time out, not proceed."
    exit 1
  fi
}

assert_allow_inprogress_proceeds() {
  local state="$1"
  MOCK_PROVISIONING_STATE="${state}"
  if ! wait_containerapp_idle "rg" "app" 1 0 allow-inprogress; then
    echo "FAIL: provisioningState=${state} with allow-inprogress should proceed (run 34635028916)."
    exit 1
  fi
}

assert_skip_first_image() {
  local state="$1"
  if ! containerapp_skip_first_image_update "${state}"; then
    echo "FAIL: provisioningState=${state} should skip the first image update."
    exit 1
  fi
}

assert_apply_first_image() {
  local state="$1"
  if containerapp_skip_first_image_update "${state}"; then
    echo "FAIL: provisioningState=${state} should apply the first image update."
    exit 1
  fi
}

assert_idle "Succeeded"
assert_idle "Failed"
assert_idle "Canceled"
assert_timeout "InProgress"
assert_timeout "Updating"
assert_timeout "Unknown"

# Run 34635028916: wait-after---no-wait must not kill config mutations.
assert_allow_inprogress_proceeds "InProgress"
assert_allow_inprogress_proceeds "Updating"
MOCK_PROVISIONING_STATE="Unknown"
if wait_containerapp_idle "rg" "app" 1 0 allow-inprogress; then
  echo "FAIL: Unknown must still be a hard timeout even with allow-inprogress."
  exit 1
fi

assert_skip_first_image "Failed"
assert_skip_first_image "Canceled"
assert_skip_first_image "InProgress"
assert_skip_first_image "Updating"
assert_apply_first_image "Succeeded"
assert_apply_first_image "Unknown"

if [ -n "$(containerapp_no_wait_if_terminal Succeeded)" ]; then
  echo "FAIL: Succeeded should not add --no-wait."
  exit 1
fi
if [ "$(containerapp_no_wait_if_terminal Failed)" != "--no-wait" ]; then
  echo "FAIL: Failed should add --no-wait."
  exit 1
fi
if [ "$(containerapp_no_wait_if_terminal Canceled)" != "--no-wait" ]; then
  echo "FAIL: Canceled should add --no-wait."
  exit 1
fi

EXPIRED='ERROR: Failed to provision revision for container app '"'"'ca-ato-copilot-mcp-v2'"'"'. Error details: Operation expired.'
if ! is_revision_lro_failure "${EXPIRED}"; then
  echo "FAIL: Operation expired should be a revision LRO failure."
  exit 1
fi
if ! is_revision_lro_failure 'error: failed to provision revision for container app. error details: operation expired.'; then
  echo "FAIL: lowercase Operation expired must match (run 34870272068)."
  exit 1
fi
if is_revision_lro_failure 'ERROR: (AuthorizationFailed) roleAssignments/write'; then
  echo "FAIL: AuthorizationFailed must not be treated as a revision LRO failure."
  exit 1
fi

# Run 34870272068: deactivate Unhealthy latestRevision, keep latestReady.
if ! containerapp_should_deactivate_revision \
  "ca-ato-copilot-mcp-v2--0000113" true Unhealthy ActivationFailed \
  "ca-ato-copilot-mcp-v2--0000087"; then
  echo "FAIL: Active Unhealthy revision that is not latestReady should deactivate."
  exit 1
fi
if containerapp_should_deactivate_revision \
  "ca-ato-copilot-mcp-v2--0000087" true Healthy Running \
  "ca-ato-copilot-mcp-v2--0000087"; then
  echo "FAIL: latestReady Healthy revision must not be deactivated."
  exit 1
fi
if containerapp_should_deactivate_revision \
  "ca-ato-copilot-mcp-v2--0000113" false Unhealthy ActivationFailed \
  "ca-ato-copilot-mcp-v2--0000087"; then
  echo "FAIL: inactive Unhealthy revision should not be deactivated."
  exit 1
fi

if ! containerapp_should_skip_ingress_port "3001" "3001"; then
  echo "FAIL: matching ingress port should skip (run 34870272068)."
  exit 1
fi
if containerapp_should_skip_ingress_port "8080" "3001"; then
  echo "FAIL: mismatched ingress port must not skip."
  exit 1
fi
if ! containerapp_should_skip_ingress_affinity "sticky" sticky; then
  echo "FAIL: sticky affinity already set should skip."
  exit 1
fi
if containerapp_should_skip_ingress_affinity "none" sticky; then
  echo "FAIL: none affinity must not skip."
  exit 1
fi
if ! containerapp_has_system_identity "SystemAssigned, UserAssigned"; then
  echo "FAIL: SystemAssigned, UserAssigned should count as present."
  exit 1
fi
if containerapp_has_system_identity "UserAssigned"; then
  echo "FAIL: UserAssigned alone must not count as system identity."
  exit 1
fi
if ! containerapp_registry_already_bound "system" system; then
  echo "FAIL: registry identity=system should be treated as already bound."
  exit 1
fi
if containerapp_registry_already_bound "" system; then
  echo "FAIL: empty registry identity must not skip registry set."
  exit 1
fi

echo "az-containerapp-mutate.test.sh: all assertions passed."
