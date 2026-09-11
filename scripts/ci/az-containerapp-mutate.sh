#!/usr/bin/env bash
# Sourced by .github/workflows/deploy-containerapp-stage.yml.
# Wait until a Container App is idle, then retry only known-transient Azure
# errors (ContainerAppOperationInProgress, HTTP 429 / Too Many Requests).
# Unexpected errors are returned to the caller — never swallowed.
#
# Refs: #872 (run 33883971872), throttle run 33771928221,
# revision LRO expire run 34387809098.

wait_containerapp_idle() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"
  local max_attempts="${3:-36}"
  local sleep_s="${4:-10}"
  local attempt=1
  local state

  while [ "${attempt}" -le "${max_attempts}" ]; do
    state="$(az containerapp show -g "${rg}" -n "${app}" --query properties.provisioningState -o tsv 2>/dev/null || echo "Unknown")"
    case "${state}" in
      Succeeded)
        echo "Container App '${app}' is idle (provisioningState=${state})."
        return 0
        ;;
      Failed|Canceled)
        # Run 34366187254: treating Failed as fatal blocked every later
        # mutation. Failed/Canceled mean the previous ARM op is finished —
        # no OperationInProgress. Waiting cannot reach Succeeded; update
        # is the recovery path (run 34300443590 left MCP Failed).
        echo "::warning::Container App '${app}' is idle in terminal state '${state}'. Proceeding so CD can recover with an update."
        return 0
        ;;
      *)
        echo "Container App '${app}' provisioningState=${state}; waiting ${sleep_s}s (attempt ${attempt}/${max_attempts})."
        sleep "${sleep_s}"
        attempt=$((attempt + 1))
        ;;
    esac
  done

  echo "::error::Timed out waiting for Container App '${app}' to leave provisioning/in-progress state."
  return 1
}

# Run 34387809098: az containerapp update on a Failed app blocked ~20m on the
# revision LRO and died with "Failed to provision revision / Operation expired".
# --no-wait accepts the ARM patch without waiting for a healthy revision
# (identity/registry are applied later; smoke check is the health gate).
# Do not delete+recreate: that rotates the system identity and re-hits #873.
containerapp_no_wait_if_terminal() {
  case "${1:-}" in
    Failed|Canceled)
      printf '%s\n' --no-wait
      ;;
  esac
}

is_revision_lro_failure() {
  printf '%s' "${1:-}" | grep -qE 'Failed to provision revision|Error details: Operation expired'
}

dump_containerapp_diagnostics() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"

  echo "=== Container App diagnostics: ${app} ==="
  if ! az containerapp show -g "${rg}" -n "${app}" \
    --query "{provisioningState:properties.provisioningState,runningStatus:properties.runningStatus,latestRevision:properties.latestRevisionName,latestReadyRevision:properties.latestReadyRevisionName,identityType:identity.type,registries:properties.configuration.registries}" \
    -o json; then
    echo "::warning::Could not show Container App '${app}' for diagnostics."
  fi
  echo "=== Revisions ==="
  if ! az containerapp revision list -g "${rg}" -n "${app}" \
    --query "[].{name:name,active:properties.active,health:properties.healthState,running:properties.runningState,replicas:properties.replicas,provisioning:properties.provisioningState,created:properties.createdTime}" \
    -o table; then
    echo "::warning::Could not list revisions for '${app}'."
  fi
}

az_containerapp_retry() {
  local max_attempts=8
  local backoff=10
  local attempt=1
  local tmp
  tmp="$(mktemp)"
  AZ_CONTAINERAPP_LAST_OUTPUT=""

  while [ "${attempt}" -le "${max_attempts}" ]; do
    if "$@" >"${tmp}" 2>&1; then
      AZ_CONTAINERAPP_LAST_OUTPUT="$(cat "${tmp}")"
      cat "${tmp}"
      rm -f "${tmp}"
      return 0
    fi
    AZ_CONTAINERAPP_LAST_OUTPUT="$(cat "${tmp}")"
    cat "${tmp}"
    if grep -qE 'ContainerAppOperationInProgress|Too Many Requests|ErrorCode: 429' "${tmp}"; then
      echo "::warning::Transient Azure error (OperationInProgress or throttle) on attempt ${attempt}/${max_attempts}; backing off ${backoff}s."
      sleep "${backoff}"
      backoff=$((backoff * 2))
      if [ "${backoff}" -gt 80 ]; then
        backoff=80
      fi
      attempt=$((attempt + 1))
      continue
    fi
    rm -f "${tmp}"
    return 1
  done

  rm -f "${tmp}"
  echo "::error::Exhausted ${max_attempts} retries for: $*"
  return 1
}

# Run 34387809098: a revision LRO timeout is not a missing app. Identity,
# AcrPull, and registry bind happen after this step; failing here left MCP
# Failed and skipped every later environment. Continue only for that LRO
# class, and only if the app resource still exists.
az_containerapp_retry_continue_revision_lro() {
  local rg="${1:?resource group required}"
  local app="${2:?container app name required}"
  shift 2

  if az_containerapp_retry "$@"; then
    return 0
  fi

  dump_containerapp_diagnostics "${rg}" "${app}"
  if is_revision_lro_failure "${AZ_CONTAINERAPP_LAST_OUTPUT:-}"; then
    if az containerapp show -g "${rg}" -n "${app}" >/dev/null 2>&1; then
      echo "::warning::Revision provision did not finish (Operation expired / Failed to provision revision). App '${app}' still exists. Continuing so later steps can bind registry/identity/secrets and reapply the image."
      return 0
    fi
    echo "::error::Revision provision expired and Container App '${app}' is gone."
    return 1
  fi
  return 1
}
