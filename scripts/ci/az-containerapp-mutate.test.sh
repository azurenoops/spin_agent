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

assert_idle "Succeeded"
assert_idle "Failed"
assert_idle "Canceled"
assert_timeout "InProgress"
assert_timeout "Updating"

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
if is_revision_lro_failure 'ERROR: (AuthorizationFailed) roleAssignments/write'; then
  echo "FAIL: AuthorizationFailed must not be treated as a revision LRO failure."
  exit 1
fi

echo "az-containerapp-mutate.test.sh: all assertions passed."
