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

echo "az-containerapp-mutate.test.sh: all assertions passed."
